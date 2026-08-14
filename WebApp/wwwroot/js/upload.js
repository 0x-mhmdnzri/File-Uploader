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
    const MIN_WORKERS = 2;
    const MAX_WORKERS_CAP = 6;
    const STORAGE_KEY = 'fileUploaderSession';
    const HASH_SLICE = 2 * 1024 * 1024; // 2 MB — constant-memory streaming hash

    /** @type {'idle'|'hashing'|'uploading'|'paused'|'verifying'|'done'|'error'} */
    let state = 'idle';
    let pauseRequested = false;
    let cancelRequested = false;
    let currentUploadId = null;
    let currentChecksum = null;
    let currentFile = null;
    let uploadedCount = 0;
    let requireChunkCrc32 = false;
    let requireChunkSha256 = false;
    let adaptiveWorkers = Math.min(Math.max(MIN_WORKERS, Math.floor((navigator.hardwareConcurrency || 4) / 2)), MAX_WORKERS_CAP);
    let activeWorkers = 0;
    let speedWindowStart = 0;
    let speedWindowBytes = 0;
    let recentMbps = [];

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

    function saveSession(meta) {
        try { localStorage.setItem(STORAGE_KEY, JSON.stringify(meta)); } catch { /* ignore */ }
    }

    function loadSession() {
        try {
            const raw = localStorage.getItem(STORAGE_KEY);
            return raw ? JSON.parse(raw) : null;
        } catch { return null; }
    }

    function clearSession() {
        try { localStorage.removeItem(STORAGE_KEY); } catch { /* ignore */ }
    }

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

    function crc32Hex(u8) {
        const table = crc32Hex._t || (crc32Hex._t = (function () {
            const t = new Uint32Array(256);
            for (let i = 0; i < 256; i++) {
                let c = i;
                for (let k = 0; k < 8; k++) c = (c & 1) ? (0xEDB88320 ^ (c >>> 1)) : (c >>> 1);
                t[i] = c;
            }
            return t;
        })());
        let crc = 0 ^ (-1);
        for (let i = 0; i < u8.length; i++) crc = (crc >>> 8) ^ table[(crc ^ u8[i]) & 0xFF];
        return ((crc ^ (-1)) >>> 0).toString(16).padStart(8, '0');
    }

    // ---- Incremental SHA-256 (constant memory; no full-file buffer) ----
    // FIPS 180-4 style pure JS compressor — processes 64-byte blocks.
    function createSha256() {
        const K = new Uint32Array([
            0x428a2f98,0x71374491,0xb5c0fbcf,0xe9b5dba5,0x3956c25b,0x59f111f1,0x923f82a4,0xab1c5ed5,
            0xd807aa98,0x12835b01,0x243185be,0x550c7dc3,0x72be5d74,0x80deb1fe,0x9bdc06a7,0xc19bf174,
            0xe49b69c1,0xefbe4786,0x0fc19dc6,0x240ca1cc,0x2de92c6f,0x4a7484aa,0x5cb0a9dc,0x76f988da,
            0x983e5152,0xa831c66d,0xb00327c8,0xbf597fc7,0xc6e00bf3,0xd5a79147,0x06ca6351,0x14292967,
            0x27b70a85,0x2e1b2138,0x4d2c6dfc,0x53380d13,0x650a7354,0x766a0abb,0x81c2c92e,0x92722c85,
            0xa2bfe8a1,0xa81a664b,0xc24b8b70,0xc76c51a3,0xd192e819,0xd6990624,0xf40e3585,0x106aa070,
            0x19a4c116,0x1e376c08,0x2748774c,0x34b0bcb5,0x391c0cb3,0x4ed8aa4a,0x5b9cca4f,0x682e6ff3,
            0x748f82ee,0x78a5636f,0x84c87814,0x8cc70208,0x90befffa,0xa4506ceb,0xbef9a3f7,0xc67178f2
        ]);
        let h0=0x6a09e667,h1=0xbb67ae85,h2=0x3c6ef372,h3=0xa54ff53a,h4=0x510e527f,h5=0x9b05688c,h6=0x1f83d9ab,h7=0x5be0cd19;
        const w = new Uint32Array(64);
        const buf = new Uint8Array(64);
        let bufLen = 0;
        let totalBitsLo = 0;
        let totalBitsHi = 0;

        function rotr(x, n) { return (x >>> n) | (x << (32 - n)); }

        function compress(block) {
            for (let i = 0; i < 16; i++) {
                w[i] = (block[i*4] << 24) | (block[i*4+1] << 16) | (block[i*4+2] << 8) | block[i*4+3];
            }
            for (let i = 16; i < 64; i++) {
                const s0 = rotr(w[i-15], 7) ^ rotr(w[i-15], 18) ^ (w[i-15] >>> 3);
                const s1 = rotr(w[i-2], 17) ^ rotr(w[i-2], 19) ^ (w[i-2] >>> 10);
                w[i] = (w[i-16] + s0 + w[i-7] + s1) | 0;
            }
            let a=h0,b=h1,c=h2,d=h3,e=h4,f=h5,g=h6,h=h7;
            for (let i = 0; i < 64; i++) {
                const S1 = rotr(e, 6) ^ rotr(e, 11) ^ rotr(e, 25);
                const ch = (e & f) ^ (~e & g);
                const t1 = (h + S1 + ch + K[i] + w[i]) | 0;
                const S0 = rotr(a, 2) ^ rotr(a, 13) ^ rotr(a, 22);
                const maj = (a & b) ^ (a & c) ^ (b & c);
                const t2 = (S0 + maj) | 0;
                h=g; g=f; f=e; e=(d + t1) | 0; d=c; c=b; b=a; a=(t1 + t2) | 0;
            }
            h0=(h0+a)|0; h1=(h1+b)|0; h2=(h2+c)|0; h3=(h3+d)|0;
            h4=(h4+e)|0; h5=(h5+f)|0; h6=(h6+g)|0; h7=(h7+h)|0;
        }

        function update(u8) {
            let off = 0;
            const len = u8.length;
            // bit length += len*8
            const bits = len * 8;
            totalBitsLo = (totalBitsLo + bits) >>> 0;
            if (totalBitsLo < bits) totalBitsHi++;
            totalBitsHi = (totalBitsHi + ((len / 0x20000000) | 0)) >>> 0;

            while (off < len) {
                const take = Math.min(64 - bufLen, len - off);
                buf.set(u8.subarray(off, off + take), bufLen);
                bufLen += take;
                off += take;
                if (bufLen === 64) {
                    compress(buf);
                    bufLen = 0;
                }
            }
        }

        function hexDigest() {
            // padding
            const pad = new Uint8Array(64);
            pad[0] = 0x80;
            const lenBits = totalBitsLo;
            const lenBitsHi = totalBitsHi;
            // copy remaining
            const tmp = new Uint8Array(bufLen);
            tmp.set(buf.subarray(0, bufLen));
            // re-run update path carefully — finalize without mutating shared state incorrectly
            let bl = bufLen;
            const block = new Uint8Array(64);
            block.set(buf.subarray(0, bl));
            block[bl++] = 0x80;
            if (bl > 56) {
                while (bl < 64) block[bl++] = 0;
                compress(block);
                block.fill(0);
                bl = 0;
            }
            while (bl < 56) block[bl++] = 0;
            // 64-bit length big-endian
            block[56] = (lenBitsHi >>> 24) & 0xff;
            block[57] = (lenBitsHi >>> 16) & 0xff;
            block[58] = (lenBitsHi >>> 8) & 0xff;
            block[59] = lenBitsHi & 0xff;
            block[60] = (lenBits >>> 24) & 0xff;
            block[61] = (lenBits >>> 16) & 0xff;
            block[62] = (lenBits >>> 8) & 0xff;
            block[63] = lenBits & 0xff;
            compress(block);

            function w4(x) {
                return [x>>>24, (x>>>16)&0xff, (x>>>8)&0xff, x&0xff]
                    .map(b => b.toString(16).padStart(2, '0')).join('');
            }
            return w4(h0)+w4(h1)+w4(h2)+w4(h3)+w4(h4)+w4(h5)+w4(h6)+w4(h7);
        }

        return { update, hexDigest };
    }

    async function sha256HexOfBuffer(u8) {
        const h = createSha256();
        h.update(u8);
        return h.hexDigest();
    }

    async function apiUploadChunk(uploadId, index, blob) {
        const headers = {};
        let body = blob;

        if (requireChunkCrc32 || requireChunkSha256) {
            const buf = await blob.arrayBuffer();
            const u8 = new Uint8Array(buf);
            if (requireChunkCrc32) headers['X-Chunk-CRC32'] = crc32Hex(u8);
            if (requireChunkSha256) headers['X-Chunk-SHA256'] = await sha256HexOfBuffer(u8);
            body = buf;
        }

        const r = await fetch(`${apiBase}/api/uploads/${uploadId}/chunk/${index}`, {
            method: 'PUT',
            headers,
            body
        });
        if (!r.ok) {
            const resBody = await r.json().catch(() => ({}));
            throw new Error(resBody.error || ('chunk ' + index + ' failed: ' + r.status));
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

    /** True streaming SHA-256: O(HASH_SLICE) memory, any file size. */
    async function computeSha256Streaming(file) {
        setStatus('Computing checksum (streaming)...');
        setProgress(0, 'Hashing...');

        const hasher = createSha256();
        const total = file.size;
        let offset = 0;

        while (offset < total) {
            if (cancelRequested) return null;
            const end = Math.min(offset + HASH_SLICE, total);
            const buf = await file.slice(offset, end).arrayBuffer();
            hasher.update(new Uint8Array(buf));
            offset = end;
            setProgress(Math.floor((offset / total) * 100), 'Hashing...');
            // yield to UI
            await new Promise(r => setTimeout(r, 0));
        }

        return hasher.hexDigest();
    }

    function noteBytes(n) {
        speedWindowBytes += n;
        const now = performance.now();
        const elapsed = (now - speedWindowStart) / 1000;
        if (elapsed >= 0.5) {
            const mbps = speedWindowBytes / (1024 * 1024) / elapsed;
            setSpeed(mbps);
            recentMbps.push(mbps);
            if (recentMbps.length > 6) recentMbps.shift();
            adaptWorkers();
            speedWindowStart = now;
            speedWindowBytes = 0;
        }
    }

    function adaptWorkers() {
        if (recentMbps.length < 3) return;
        const avg = recentMbps.reduce((a, b) => a + b, 0) / recentMbps.length;
        if (avg > 20 && adaptiveWorkers < MAX_WORKERS_CAP) adaptiveWorkers++;
        else if (avg < 4 && adaptiveWorkers > MIN_WORKERS) adaptiveWorkers--;
    }

    async function runUpload(file, uploadId, totalChunks, checksum, alreadyReceived) {
        currentFile = file;
        currentUploadId = uploadId;
        currentChecksum = checksum;
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
        setStatus(`Uploading ${uploadedCount}/${totalChunks} chunks (${adaptiveWorkers} workers)`);
        setState('uploading');
        speedWindowStart = performance.now();
        speedWindowBytes = 0;
        activeWorkers = 0;

        saveSession({
            uploadId,
            fileName: file.name,
            fileSize: file.size,
            totalChunks,
            checksum,
            savedAt: Date.now()
        });

        let fatalError = null;

        function pump() {
            while (
                activeWorkers < adaptiveWorkers &&
                queue.length > 0 &&
                !cancelRequested &&
                !pauseRequested &&
                !fatalError
            ) {
                activeWorkers++;
                workerOne().finally(() => {
                    activeWorkers--;
                    if (!fatalError && !cancelRequested && !pauseRequested) pump();
                });
            }
        }

        async function workerOne() {
            while (!cancelRequested && !pauseRequested && !fatalError) {
                // Shrink: if over target concurrency, exit this worker.
                if (activeWorkers > adaptiveWorkers) return;

                const item = queue.shift();
                if (!item) return;

                try {
                    const blob = file.slice(item.start, item.end);
                    await apiUploadChunk(uploadId, item.index, blob);
                    received.add(item.index);
                    uploadedCount++;
                    noteBytes(item.end - item.start);
                    setProgress(Math.floor((uploadedCount / totalChunks) * 100));
                    setStatus(`Uploading ${uploadedCount}/${totalChunks} chunks (${adaptiveWorkers} workers)`);
                } catch (e) {
                    item.retries++;
                    if (item.retries <= 3 && !cancelRequested) {
                        queue.push(item);
                        await new Promise(r => setTimeout(r, 400 * item.retries));
                    } else if (!cancelRequested) {
                        fatalError = e;
                        return;
                    }
                }
            }
        }

        pump();

        // Wait until idle: no active workers and (queue empty or stopped).
        while (activeWorkers > 0 || (queue.length > 0 && !cancelRequested && !pauseRequested && !fatalError)) {
            if (fatalError) break;
            // Allow scale-up mid-flight
            if (!cancelRequested && !pauseRequested && !fatalError) pump();
            await new Promise(r => setTimeout(r, 50));
        }

        if (fatalError) throw fatalError;

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
            const checksum = await computeSha256Streaming(file);
            if (checksum) console.log('[UPLOAD] SHA-256:', checksum);

            setStatus('Initiating...');
            const init = await apiInitiate(file, checksum);
            requireChunkCrc32 = !!init.requireChunkCrc32;
            requireChunkSha256 = !!init.requireChunkSha256;
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

    startBtn.addEventListener('click', () => {
        if (state === 'idle' || state === 'done' || state === 'error') startFresh();
    });

    if (pauseBtn) {
        pauseBtn.addEventListener('click', () => {
            if (state === 'uploading') {
                pauseRequested = true;
                setStatus('Pausing...');
            } else if (state === 'paused' && currentFile && currentUploadId) {
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
