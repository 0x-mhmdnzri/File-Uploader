window.uploaderInit = function (apiBase) {
    const fileInput = document.getElementById('file');
    const startBtn = document.getElementById('start');
    const pauseBtn = document.getElementById('pause');
    const cancelBtn = document.getElementById('cancel');
    const progressBar = document.getElementById('progress-bar');
    const statusText = document.getElementById('status-text');
    const speedText = document.getElementById('speed-text');
    const errorBox = document.getElementById('error-box');
    const resumeBanner = document.getElementById('resume-banner');
    const resumeText = document.getElementById('resume-text');
    const resumeBtn = document.getElementById('resume-btn');
    const discardBtn = document.getElementById('discard-btn');

    if (!fileInput || !startBtn || !progressBar) return;

    const CHUNK_SIZE = 16 * 1024 * 1024;
    const MAX_WORKERS = Math.min(Math.max(1, Math.floor((navigator.hardwareConcurrency || 4) / 2)), 6);
    const STORAGE_KEY = 'fileUploaderSession';

    /** @type {'idle'|'hashing'|'uploading'|'paused'|'verifying'|'done'|'error'} */
    let state = 'idle';
    let pauseRequested = false;
    let cancelRequested = false;
    let currentUploadId = null;
    let currentChecksum = null;
    let currentFile = null;
    let currentTotalChunks = 0;
    let uploadedCount = 0;
    let bytesSentSession = 0;
    let speedWindowStart = 0;
    let speedWindowBytes = 0;

    // ---------- UI helpers ----------

    function setProgress(percent, label) {
        const p = Math.max(0, Math.min(100, percent | 0));
        progressBar.style.width = p + '%';
        progressBar.setAttribute('aria-valuenow', String(p));
        progressBar.textContent = label || (p + '%');
    }

    function setStatus(msg) {
        if (statusText) statusText.textContent = msg;
    }

    function setSpeed(mbps) {
        if (!speedText) return;
        speedText.textContent = mbps == null ? '' : mbps.toFixed(1) + ' MB/s';
    }

    function showError(msg) {
        if (!errorBox) return;
        errorBox.textContent = msg;
        errorBox.classList.remove('d-none');
    }

    function clearError() {
        if (!errorBox) return;
        errorBox.textContent = '';
        errorBox.classList.add('d-none');
    }

    function setButtons() {
        const busy = state === 'hashing' || state === 'uploading' || state === 'verifying';
        startBtn.disabled = busy || state === 'paused';
        if (pauseBtn) {
            pauseBtn.disabled = state !== 'uploading' && state !== 'paused';
            pauseBtn.textContent = state === 'paused' ? 'Resume' : 'Pause';
        }
        if (cancelBtn) cancelBtn.disabled = !(busy || state === 'paused');
        fileInput.disabled = busy || state === 'paused';
    }

    function setState(s) {
        state = s;
        setButtons();
    }

    // ---------- persistence ----------

    function saveSession(meta) {
        try {
            localStorage.setItem(STORAGE_KEY, JSON.stringify(meta));
        } catch { /* ignore */ }
    }

    function loadSession() {
        try {
            const raw = localStorage.getItem(STORAGE_KEY);
            return raw ? JSON.parse(raw) : null;
        } catch {
            return null;
        }
    }

    function clearSession() {
        try { localStorage.removeItem(STORAGE_KEY); } catch { /* ignore */ }
    }

    // ---------- API ----------

    async function apiInitiate(file, checksum) {
        const fd = new FormData();
        fd.append('fileName', file.name);
        fd.append('totalSize', file.size);
        fd.append('chunkSize', CHUNK_SIZE);
        if (file.type) fd.append('contentType', file.type);
        if (checksum) fd.append('checksum', checksum);

        const r = await fetch(`${apiBase}/api/uploads/initiate`, { method: 'POST', body: fd });
        const body = await r.json().catch(() => ({}));
        if (!r.ok) throw new Error(body.error || ('initiate failed: ' + r.status));
        return body;
    }

    async function apiUploadChunk(uploadId, index, blob) {
        const r = await fetch(`${apiBase}/api/uploads/${uploadId}/chunk/${index}`, {
            method: 'PUT',
            body: blob
        });
        if (!r.ok) {
            const body = await r.json().catch(() => ({}));
            throw new Error(body.error || ('chunk ' + index + ' failed: ' + r.status));
        }
    }

    async function apiComplete(uploadId, checksum) {
        const fd = new FormData();
        if (checksum) fd.append('checksum', checksum);
        const r = await fetch(`${apiBase}/api/uploads/${uploadId}/complete`, { method: 'POST', body: fd });
        const body = await r.json().catch(() => ({}));
        if (!r.ok) throw new Error(body.error || ('complete failed: ' + r.status));
        return body;
    }

    async function apiAbort(uploadId) {
        await fetch(`${apiBase}/api/uploads/${uploadId}`, { method: 'DELETE' });
    }

    async function apiStatus(uploadId) {
        const r = await fetch(`${apiBase}/api/uploads/${uploadId}/status`);
        if (r.status === 404) return null;
        if (!r.ok) throw new Error('status failed: ' + r.status);
        return await r.json();
    }

    // ---------- checksum ----------

    async function computeSha256(file) {
        const cryptoSubtle = window.crypto?.subtle;
        if (!cryptoSubtle) return null;
        if (file.size > 512 * 1024 * 1024) {
            console.warn('[UPLOAD] File > 512MB; skipping client-side checksum');
            return null;
        }
        setStatus('Computing checksum...');
        setProgress(0, 'Hashing...');
        const buffer = await file.arrayBuffer();
        const hashBuffer = await cryptoSubtle.digest('SHA-256', buffer);
        return Array.from(new Uint8Array(hashBuffer))
            .map(b => b.toString(16).padStart(2, '0'))
            .join('');
    }

    // ---------- core upload loop ----------

    function noteBytes(n) {
        bytesSentSession += n;
        speedWindowBytes += n;
        const now = performance.now();
        const elapsed = (now - speedWindowStart) / 1000;
        if (elapsed >= 0.5) {
            setSpeed(speedWindowBytes / (1024 * 1024) / elapsed);
            speedWindowStart = now;
            speedWindowBytes = 0;
        }
    }

    async function runUpload(file, uploadId, totalChunks, checksum, alreadyReceived) {
        currentFile = file;
        currentUploadId = uploadId;
        currentChecksum = checksum;
        currentTotalChunks = totalChunks;
        cancelRequested = false;
        pauseRequested = false;

        const received = new Set(alreadyReceived || []);
        uploadedCount = received.size;

        const queue = [];
        for (let i = 0; i < totalChunks; i++) {
            if (!received.has(i)) {
                const start = i * CHUNK_SIZE;
                const end = Math.min(start + CHUNK_SIZE, file.size);
                queue.push({ index: i, start, end, retries: 0 });
            }
        }

        setProgress(Math.floor((uploadedCount / totalChunks) * 100));
        setStatus(`Uploading ${uploadedCount}/${totalChunks} chunks`);
        setState('uploading');
        speedWindowStart = performance.now();
        speedWindowBytes = 0;
        bytesSentSession = 0;

        saveSession({
            uploadId,
            fileName: file.name,
            fileSize: file.size,
            totalChunks,
            checksum,
            savedAt: Date.now()
        });

        async function worker() {
            while (true) {
                if (cancelRequested) return;
                if (pauseRequested) return;

                const item = queue.shift();
                if (!item) return;

                try {
                    const blob = file.slice(item.start, item.end);
                    await apiUploadChunk(uploadId, item.index, blob);
                    received.add(item.index);
                    uploadedCount++;
                    noteBytes(item.end - item.start);
                    setProgress(Math.floor((uploadedCount / totalChunks) * 100));
                    setStatus(`Uploading ${uploadedCount}/${totalChunks} chunks`);
                } catch (e) {
                    item.retries++;
                    if (item.retries <= 3 && !cancelRequested) {
                        queue.push(item);
                        await new Promise(r => setTimeout(r, 400 * item.retries));
                    } else if (!cancelRequested) {
                        throw e;
                    }
                }
            }
        }

        const workers = Array.from({ length: MAX_WORKERS }, () => worker());
        await Promise.all(workers);

        if (cancelRequested) {
            setStatus('Cancelled');
            setState('idle');
            return;
        }

        if (pauseRequested) {
            setStatus(`Paused — ${uploadedCount}/${totalChunks} chunks`);
            setState('paused');
            setSpeed(null);
            return;
        }

        // Complete
        setState('verifying');
        setProgress(99, 'Verifying...');
        setStatus('Verifying checksum...');
        setSpeed(null);

        const result = await apiComplete(uploadId, checksum);
        clearSession();
        setProgress(100, '100% — done');
        setStatus('Completed: ' + (result.path || file.name));
        setState('done');
        currentUploadId = null;
    }

    async function startFresh() {
        clearError();
        const file = fileInput.files[0];
        if (!file) {
            showError('Please select a file first.');
            return;
        }

        try {
            setState('hashing');
            const checksum = await computeSha256(file);
            if (checksum) console.log('[UPLOAD] SHA-256:', checksum);

            setStatus('Initiating...');
            const init = await apiInitiate(file, checksum);
            await runUpload(file, init.uploadId, init.totalChunks, checksum, []);
        } catch (err) {
            console.error('[UPLOAD]', err);
            showError(err.message || String(err));
            setStatus('Error');
            setState('error');
            if (currentUploadId) {
                try { await apiAbort(currentUploadId); } catch { /* ignore */ }
                currentUploadId = null;
            }
            clearSession();
        } finally {
            if (state === 'error' || state === 'done') setButtons();
        }
    }

    async function resumeFromServer(uploadId, file) {
        clearError();
        const st = await apiStatus(uploadId);
        if (!st || st.status !== 'Pending' || st.isExpired) {
            clearSession();
            showError('Previous session is no longer available. Start a new upload.');
            hideResumeBanner();
            return;
        }

        if (file.size !== st.totalSize) {
            showError('Selected file size does not match the previous upload. Choose the same file.');
            return;
        }

        const checksum = st.checksum || null;
        await runUpload(file, uploadId, st.totalChunks, checksum, st.received || []);
    }

    function hideResumeBanner() {
        if (resumeBanner) resumeBanner.classList.add('d-none');
    }

    // ---------- events ----------

    startBtn.addEventListener('click', () => {
        if (state === 'idle' || state === 'done' || state === 'error') startFresh();
    });

    if (pauseBtn) {
        pauseBtn.addEventListener('click', () => {
            if (state === 'uploading') {
                pauseRequested = true;
                setStatus('Pausing...');
            } else if (state === 'paused' && currentFile && currentUploadId) {
                // resume local queue by re-fetching status and continuing
                pauseRequested = false;
                cancelRequested = false;
                (async () => {
                    try {
                        const st = await apiStatus(currentUploadId);
                        if (!st || st.status !== 'Pending') {
                            showError('Session is no longer pending.');
                            setState('idle');
                            return;
                        }
                        await runUpload(
                            currentFile,
                            currentUploadId,
                            st.totalChunks,
                            currentChecksum,
                            st.received || []
                        );
                    } catch (err) {
                        showError(err.message || String(err));
                        setState('error');
                    }
                })();
            }
        });
    }

    if (cancelBtn) {
        cancelBtn.addEventListener('click', async () => {
            cancelRequested = true;
            pauseRequested = false;
            setStatus('Cancelling...');
            const id = currentUploadId;
            if (id) {
                try { await apiAbort(id); } catch { /* ignore */ }
            }
            clearSession();
            currentUploadId = null;
            setProgress(0, '0%');
            setSpeed(null);
            setStatus('Cancelled');
            setState('idle');
            hideResumeBanner();
        });
    }

    // ---------- resume banner on load ----------

    (async function checkResume() {
        const session = loadSession();
        if (!session?.uploadId) return;

        try {
            const st = await apiStatus(session.uploadId);
            if (!st || st.status !== 'Pending' || st.isExpired) {
                clearSession();
                return;
            }
            if (resumeBanner && resumeText) {
                const pct = st.totalChunks
                    ? Math.floor(((st.receivedCount || 0) / st.totalChunks) * 100)
                    : 0;
                resumeText.textContent =
                    `Unfinished upload: "${session.fileName}" (${pct}% — ${st.receivedCount || 0}/${st.totalChunks} chunks). Select the same file and click Resume.`;
                resumeBanner.classList.remove('d-none');
            }

            if (resumeBtn) {
                resumeBtn.onclick = async () => {
                    const file = fileInput.files[0];
                    if (!file) {
                        showError('Select the same file that was being uploaded, then click Resume.');
                        return;
                    }
                    if (file.name !== session.fileName || file.size !== session.fileSize) {
                        showError('File name/size does not match the previous session.');
                        return;
                    }
                    hideResumeBanner();
                    try {
                        await resumeFromServer(session.uploadId, file);
                    } catch (err) {
                        showError(err.message || String(err));
                        setState('error');
                    }
                };
            }

            if (discardBtn) {
                discardBtn.onclick = async () => {
                    try { await apiAbort(session.uploadId); } catch { /* ignore */ }
                    clearSession();
                    hideResumeBanner();
                    setStatus('Previous upload discarded');
                };
            }
        } catch {
            clearSession();
        }
    })();

    setState('idle');
    setStatus('Ready');
};