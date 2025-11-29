
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

    const CHUNK_SIZE = 0 * 1024 * 1024; // 16 MB
    const MAX_WORKERS = Math.min(Math.floor(navigator.hardwareConcurrency / 2), 16);

    async function initiate(file) {
        const fd = new FormData();
        fd.append('fileName', file.name);
        fd.append('totalSize', file.size);
        fd.append('chunkSize', CHUNK_SIZE);
        const r = await fetch(`${apiBase}/api/uploads/initiate`, { method: 'POST', body: fd });
        return await r.json();
    }

    async function uploadChunk(uploadId, index, blob) {
        const url = `${apiBase}/api/uploads/${uploadId}/chunk/${index}`;
        const r = await fetch(url, { method: 'PUT', body: blob });
        if (!r.ok) throw new Error("chunk failed " + index);
    }

    async function complete(uploadId) {
        await fetch(`${apiBase}/api/uploads/${uploadId}/complete`, { method: 'POST' });
    }

    async function status(uploadId) {
        const r = await fetch(`${apiBase}/api/uploads/${uploadId}/status`);
        if (r.status === 404) return null;
        return await r.json();
    }

    startBtn.addEventListener('click', async () => {
        const file = fileInput.files[0];
        if (!file) return;

        const init = await initiate(file);
        const uploadId = init.uploadId;
        const totalChunks = init.totalChunks;

        updateProgress(0);

        const received = new Set();
        const existing = await status(uploadId);
        if (existing?.received) existing.received.forEach(i => received.add(i));

        let uploaded = received.size;

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

        await complete(uploadId);
        updateProgress(100);
        progressBar.textContent = "100% - done";
    });
};
