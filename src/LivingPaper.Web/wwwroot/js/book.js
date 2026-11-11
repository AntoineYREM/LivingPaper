// Refreshes the progress of a book while its analysis runs, and reloads the page when it stops moving (done, failed or
// interrupted), so the characters, or the error, appear without any action. The page works without this script: a reload
// shows the same thing.
(() => {
  const box = document.getElementById('book-progress');
  if (!box || !box.dataset.refreshUrl) {
    return;
  }

  const everyMs = 3000;
  let timer;

  const schedule = () => {
    clearTimeout(timer);
    timer = setTimeout(refresh, everyMs);
  };

  async function refresh() {
    if (!document.hidden) {
      try {
        const response = await fetch(box.dataset.refreshUrl, { cache: 'no-store', headers: { 'X-Requested-With': 'fetch' } });
        if (response.ok) {
          const html = await response.text();
          box.innerHTML = html;
          if (box.querySelector('[data-state]')?.dataset.state !== 'active') {
            location.reload();
            return;
          }
        }
      } catch {
        // The server is not reachable for the moment: try again at the next turn.
      }
    }

    schedule();
  }

  document.addEventListener('visibilitychange', schedule);
  schedule();
})();
