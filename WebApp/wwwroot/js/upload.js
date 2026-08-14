
window.uploaderInit = function(apiBase) {
    console.log("[UPLOAD] init called with:", apiBase);

    const fileInput = document.getElementById('file');
    const startBtn = document.getElementById('start');
    const progressBar = document.getElementById('progress-bar');

    if (!fileInput || !startBtn || !progressBar) return;

    function updateProgress(percent) {
        progressBar.style.width = percent + "%";
        progressBar.setAttribute("aria-valuenow", percent);
        progressBar.textContent = percent + "%";
    }

    // 16 MB is still optimal for most networks/disks
    const CHUNK_SIZE = 16 * 1024 * 1024;

    // More aggressive but still safe parallelism
    const MAX_WORKERS = Math.min(Math.max(4, Math.floor(navigator.hardwareConcurrency)), 12);

    async function initiate(file) {
        const fd = new FormData();
        fd.append('fileName', file.name);
        fd.append('totalSize', file.size);
        fd.append('chunkSize', CHUNK_SIZE);
        const r = await fetch(`${apiBase}/api/uploads/initiate`, { method: 'POST', body: fd });
        if (!r.ok) {
            const err = await r.json().catch(() => ({}));
            throw new Error(err.error || `Initiate failed: ${r.status}`);
        }
        return await r.json();
    }

    async function uploadChunk(uploadId, index, blob) {
        const url = `${apiBase}/api/uploads/${uploadId}/chunk/${index}`;
        const r = await fetch(url, {
            method: 'PUT',
            body: blob,
            // Keep connection alive / avoid extra headers that hurt latency
        });
        if (!r.ok) throw new Error(`chunk ${index} failed (${r.status})`);
    }

    async function complete(uploadId) {
        const r = await fetch(`${apiBase}/api/uploads/${uploadId}/complete`, { method: 'POST' });
        if (!r.ok) {
            const err = await r.json().catch(() => ({}));
            throw new Error(err.error || `Complete failed: ${r.status}`);
        }
    }

    async function status(uploadId) {
        const r = await fetch(`${apiBase}/api/uploads/${uploadId}/status`);
        if (r.status === 404) return null;
        if (!r.ok) throw new Error(`Status failed: ${r.status}`);
        return await r.json();
    }

    // Simple concurrent-safe queue (mimics ConcurrentQueue + SemaphoreSlim)
    function createWorkQueue(items, concurrency) {
        let index = 0;
        const results = new Array(items.length);
        let active = 0;
        let resolveAll, rejectAll;
        const done = new Promise((res, rej) => { resolveAll = res; rejectAll = rej; });

        function next() {
            if (index >= items.length && active === 0) {
                resolveAll(results);
                return;
            }
            while (active < concurrency && index < items.length) {
                const i = index++;
                const item = items[i];
                active++;
                item()
                    .then(r => { results[i] = r; })
                    .catch(e => { rejectAll(e); })
                    .finally(() => {
                        active--;
                        next();
                    });
            }
        }
        next();
        return done;
    }

    startBtn.addEventListener('click', async () => {
        const file = fileInput.files[0];
        if (!file) return;

        startBtn.disabled = true;
        try {
            console.log(`[UPLOAD] Starting ${file.name} (${(file.size / 1024 / 1024).toFixed(1)} MB) with ${MAX_WORKERS} workers`);

            const init = await initiate(file);
            const uploadId = init.uploadId;
            const totalChunks = init.totalChunks;

            updateProgress(0);

            // Resume support
            const received = new Set();
            const existing = await status(uploadId);
            if (existing?.received) existing.received.forEach(i => received.add(i));

            let uploaded = received.size;
            updateProgress(Math.floor((uploaded / totalChunks) * 100));

            // Build work items
            const workItems = [];
            for (let i = 0; i < totalChunks; i++) {
                if (received.has(i)) continue;

                const start = i * CHUNK_SIZE;
                const end = Math.min(start + CHUNK_SIZE, file.size);
                const index = i;

                workItems.push(async () => {
                    let retries = 0;
                    const maxRetries = 4;

                    while (true) {
                        try {
                            const blob = file.slice(start, end);
                            await uploadChunk(uploadId, index, blob);
                            uploaded++;
                            updateProgress(Math.floor((uploaded / totalChunks) * 100));
                            return index;
                        } catch (e) {
                            retries++;
                            if (retries > maxRetries) throw e;
                            // exponential backoff + jitter
                            const delay = Math.min(2000, 200 * Math.pow(2, retries)) + Math.random() * 200;
                            await new Promise(r => setTimeout(r, delay));
                        }
                    }
                });
            }

            // Parallel execution with controlled concurrency (SemaphoreSlim equivalent)
            await createWorkQueue(workItems, MAX_WORKERS);

            // ---- Client-side verification before complete ----
            const finalStatus = await status(uploadId);
            if (!finalStatus) throw new Error("Session disappeared");

            const serverReceived = new Set(finalStatus.received || []);
            const missing = [];
            for (let i = 0; i < totalChunks; i++) {
                if (!serverReceived.has(i)) missing.push(i);
            }

            if (missing.length > 0) {
                console.error("[UPLOAD] Missing chunks after upload:", missing.slice(0, 20));
                throw new Error(`Verification failed: ${missing.length} chunks missing on server`);
            }

            // All good → complete (server does its own ConcurrentBag verification too)
            await complete(uploadId);

            updateProgress(100);
            progressBar.textContent = "100% - verified & done";
            console.log("[UPLOAD] Complete and verified");
        } catch (err) {
            console.error("[UPLOAD] Failed:", err);
            progressBar.textContent = "Error: " + (err.message || err);
            progressBar.style.backgroundColor = "#dc3545";
        } finally {
            startBtn.disabled = false;
        }
    });
};
