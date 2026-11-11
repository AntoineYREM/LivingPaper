// Tells the style sheets that JavaScript runs, so what needs it (the chat buttons) is shown only then.
// It is the one script that is not deferred, so the buttons never flash in and out; every page works without it.
document.documentElement.classList.add('js');
