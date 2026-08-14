window.uploaderInit = function (apiBase) {
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

    const CHUNK_SIZE = 16 * 1024 * 1024; // 16 MB
    const MAX_WORKERS = Math.min(Math.floor(navigator.hardwareConcurrency / 2), 6);

    async function initiate(file) {
        const fd = new FormData();
        fd.append('fileName', file.name);
        fd.append('totalSize', file.size);
        fd.append('chunkSize', CHUNK_SIZE);
        if (file.type) fd.append('contentType', file.type);

        const r = await fetch(`${apiBase}/api/uploads/initiate`, { method: 'POST', body: fd });
        if (!r.ok) throw new Error('initiate failed: ' + r.status);
        return await r.json();
    }

    async function uploadChunk(uploadId, index, blob) {
        const url = `${apiBase}/api/uploads/${uploadId}/chunk/${index}`;
        const r = await fetch(url, { method: 'PUT', body: blob });
        if (!r.ok) throw new Error("chunk failed " + index);
    }

    async function complete(uploadId) {
        const r = await fetch(`${apiBase}/api/uploads/${uploadId}/complete`, { method: 'POST' });
        if (!r.ok) throw new Error('complete failed: ' + r.status);
        return await r.json();
    }

    async function abort(uploadId) {
        await fetch(`${apiBase}/api/uploads/${uploadId}`, { method: 'DELETE' });
    }

    async function status(uploadId) {
        const r = await fetch(`${apiBase}/api/uploads/${uploadId}/status`);
        if (r.status === 404) return null;
        if (!r.ok) throw new Error('status failed: ' + r.status);
        return await r.json();
    }

    startBtn.addEventListener('click', async () => {
        const file = fileInput.files[0];
        if (!file) return;

        startBtn.disabled = true;
        updateProgress(0);

        let uploadId = null;

        try {
            const init = await initiate(file);
            uploadId = init.uploadId;
            const totalChunks = init.totalChunks;

            // Persist for possible future resume support
            try {
                localStorage.setItem('lastUploadId', uploadId);
                localStorage.setItem('lastUploadFileName', file.name);
            } catch { /* ignore */ }

            const received = new Set();
            const existing = await status(uploadId);
            if (existing?.received) existing.received.forEach(i => received.add(i));

            let uploaded = received.size;
            updateProgress(Math.floor((uploaded / totalChunks) * 100));

            const queue = [];
            for (let i = 0; i < totalChunks; i++) {
                if (!received.has(i)) {
                    const start = i * CHUNK_SIZE;
                    const end = Math.min(start + CHUNK_SIZE, file.size);
                    queue.push({ index: i, blob: file.slice(start, end), retries: 0 });
                }
            }

            async function worker() {
                while (queue.length > 0) {
                    const item = queue.shift();
                    try {
                        await uploadChunk(uploadId, item.index, item.blob);
                        uploaded++;
                        updateProgress(Math.floor((uploaded / totalChunks) * 100));
                    } catch (e) {
                        console.error("chunk error", item.index, e);
                        item.retries++;
                        if (item.retries <= 3) {
                            queue.push(item);
                            await new Promise(r => setTimeout(r, 500 * item.retries));
                        } else {
                            throw new Error(`Chunk ${item.index} failed after 3 retries`);
                        }
                    }
                }
            }

            const workers = Array.from({ length: MAX_WORKERS }, () => worker());
            await Promise.all(workers);

            const result = await complete(uploadId);
            updateProgress(100);
            progressBar.textContent = "100% - done";
            console.log("[UPLOAD] completed", result);

        } catch (err) {
            console.error("[UPLOAD] failed", err);
            progressBar.textContent = "Error";

            // Best-effort abort so server can clean temp files
            if (uploadId) {
                try { await abort(uploadId); } catch { /* ignore */ }
            }

            alert("Upload failed: " + (err.message || err));
        } finally {
            startBtn.disabled = false;
        }
    });
};
