window.uploaderInit = function (apiBase) {
    console.log("[UPLOAD] init called with:", apiBase);

    const fileInput = document.getElementById('file');
    const startBtn = document.getElementById('start');
    const progressBar = document.getElementById('progress-bar');

    if (!fileInput || !startBtn || !progressBar) return;

    function updateProgress(percent, label) {
        progressBar.style.width = percent + "%";
        progressBar.setAttribute("aria-valuenow", percent);
        progressBar.textContent = label || (percent + "%");
    }

    const CHUNK_SIZE = 16 * 1024 * 1024; // 16 MB
    const MAX_WORKERS = Math.min(Math.floor(navigator.hardwareConcurrency / 2), 6);

    /** Compute SHA-256 of a File/Blob, returns lowercase hex string */
    async function computeSha256(file) {
        // For large files, hash in chunks to avoid loading everything into memory
        const chunkSize = 8 * 1024 * 1024; // 8 MB
        let offset = 0;
        const cryptoSubtle = window.crypto?.subtle;

        if (!cryptoSubtle) {
            console.warn("[UPLOAD] Web Crypto not available; skipping checksum");
            return null;
        }

        // Incremental hashing via SubtleCrypto is not directly supported,
        // so we read the whole file as ArrayBuffer for moderate sizes.
        // For very large files (>512MB) we skip client-side hash to avoid memory pressure.
        if (file.size > 512 * 1024 * 1024) {
            console.warn("[UPLOAD] File > 512MB; skipping client-side checksum");
            return null;
        }

        updateProgress(0, "Hashing...");
        const buffer = await file.arrayBuffer();
        const hashBuffer = await cryptoSubtle.digest("SHA-256", buffer);
        const hashArray = Array.from(new Uint8Array(hashBuffer));
        return hashArray.map(b => b.toString(16).padStart(2, "0")).join("");
    }

    async function initiate(file, checksum) {
        const fd = new FormData();
        fd.append('fileName', file.name);
        fd.append('totalSize', file.size);
        fd.append('chunkSize', CHUNK_SIZE);
        if (file.type) fd.append('contentType', file.type);
        if (checksum) fd.append('checksum', checksum);

        const r = await fetch(`${apiBase}/api/uploads/initiate`, { method: 'POST', body: fd });
        if (!r.ok) throw new Error('initiate failed: ' + r.status);
        return await r.json();
    }

    async function uploadChunk(uploadId, index, blob) {
        const url = `${apiBase}/api/uploads/${uploadId}/chunk/${index}`;
        const r = await fetch(url, { method: 'PUT', body: blob });
        if (!r.ok) throw new Error("chunk failed " + index);
    }

    async function complete(uploadId, checksum) {
        const fd = new FormData();
        if (checksum) fd.append('checksum', checksum);

        const r = await fetch(`${apiBase}/api/uploads/${uploadId}/complete`, {
            method: 'POST',
            body: fd
        });
        if (!r.ok) {
            const text = await r.text();
            throw new Error('complete failed: ' + r.status + ' ' + text);
        }
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
        let checksum = null;

        try {
            // 1) Optional client-side checksum
            checksum = await computeSha256(file);
            if (checksum) {
                console.log("[UPLOAD] client SHA-256:", checksum);
            }

            // 2) Initiate
            const init = await initiate(file, checksum);
            uploadId = init.uploadId;
            const totalChunks = init.totalChunks;

            try {
                localStorage.setItem('lastUploadId', uploadId);
                localStorage.setItem('lastUploadFileName', file.name);
            } catch { /* ignore */ }

            // 3) Resume support
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

            // 4) Complete + server-side verify
            updateProgress(99, "Verifying...");
            const result = await complete(uploadId, checksum);
            updateProgress(100, "100% - done");
            console.log("[UPLOAD] completed", result);

        } catch (err) {
            console.error("[UPLOAD] failed", err);
            progressBar.textContent = "Error";

            if (uploadId) {
                try { await abort(uploadId); } catch { /* ignore */ }
            }

            alert("Upload failed: " + (err.message || err));
        } finally {
            startBtn.disabled = false;
        }
    });
};
