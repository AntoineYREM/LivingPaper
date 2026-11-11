// Refreshes the list of the books while an analysis is waiting or running, so the progress moves without a reload.
// The page works without this script: the list is just not refreshed, and a reload shows the same thing.
(() => {
  const list = document.getElementById('book-list');
  if (!list || !list.dataset.refreshUrl) {
    return;
  }

  const everyMs = 5000;
  let timer;

  // Something is still moving: a book is waiting or running.
  const active = () => list.querySelector('[data-analysis-active]') !== null;

  // Never replace the list under the reader's hands: an open confirmation of a deletion, or a focus inside the list.
  const busy = () => document.hidden || list.querySelector('details[open]') !== null || list.contains(document.activeElement);

  const schedule = () => {
    clearTimeout(timer);
    if (active()) {
      timer = setTimeout(refresh, everyMs);
    }
  };

  async function refresh() {
    if (!busy()) {
      try {
        const response = await fetch(list.dataset.refreshUrl, { cache: 'no-store', headers: { 'X-Requested-With': 'fetch' } });
        if (response.ok) {
          list.innerHTML = await response.text();
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
