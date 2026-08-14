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

    // 16 MB is still optimal for most networks/disks
    const CHUNK_SIZE = 16 * 1024 * 1024;

    // More aggressive but still safe parallelism
    const MAX_WORKERS = Math.min(Math.max(4, Math.floor(navigator.hardwareConcurrency)), 12);

    async function initiate(file) {
        const fd = new FormData();
        fd.append('fileName', file.name);
        fd.append('totalSize', file.size);
        fd.append('chunkSize', CHUNK_SIZE);
        const r = await fetch(`${apiBase}/api/client/upload/initiate`, {method: 'POST', body: fd});
        return await r.json();
    }

    async function uploadChunk(uploadId, index, blob) {
        const url = `${apiBase}/api/client/upload/${uploadId}/chunk/${index}`;
        const r = await fetch(url, {method: 'PUT', body: blob});
        if (!r.ok) throw new Error("chunk failed " + index);
    }

    async function complete(uploadId) {
        const r = await fetch(`${apiBase}/api/client/upload/${uploadId}/complete`, {method: 'POST'});
        try {
            return await r.json();   // انتظار JSON
        } catch {
            return {message: "Upload completed (no JSON returned)"};
        }
    }

    async function status(uploadId) {
        const r = await fetch(`${apiBase}/api/client/upload/${uploadId}/status`);
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
                queue.push({index: i, blob: file.slice(start, end), retries: 0});
            }
        }

        async function worker() {
            while (queue.length > 0) {
                const item = queue.shift();
                try {
                    console.log("UPLOAD ID IS:", uploadId);
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


        const workers = Array.from({length: MAX_WORKERS}, () => worker());
        await Promise.all(workers);

        const result = await complete(uploadId);

        updateProgress(100);
        progressBar.textContent = "100% - done";

        alert("UPLOAD COMPLETED!:" + uploaded);
        console.log("[UPLOAD RESULT]", result);

    });
};
