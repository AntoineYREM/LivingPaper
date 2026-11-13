// The chat with a character. The conversation lives in this page only: leaving it ends the conversation, nothing is stored.
// Each message is sent with the whole conversation (POST, JSON); the reply comes back as NDJSON, one JSON object per line,
// and is written into its box as it arrives:
//   {"type":"text","text":"..."}  a piece of the reply   {"type":"error","code":"..."}  it stopped   {"type":"done"}  complete
// What is shown comes from the page (chat-texts); nothing is written as HTML: texts go in with textContent.
(() => {
  const root = document.getElementById('chat');
  const textsNode = document.getElementById('chat-texts');
  if (!root || !textsNode) {
    return;
  }

  const texts = JSON.parse(textsNode.textContent);
  const log = document.getElementById('chat-log');
  const form = document.getElementById('chat-form');
  const field = document.getElementById('chat-message');
  const sendButton = document.getElementById('chat-send');
  const jump = document.getElementById('chat-jump');
  const errorBox = document.getElementById('chat-error');
  const errorText = document.getElementById('chat-error-text');
  const token = root.querySelector('input[name="__RequestVerificationToken"]').value;
  const messages = [];
  let busy = false;

  const errorTitle = errorBox.querySelector('.alert__title');

  const showError = (text, title) => {
    errorTitle.textContent = title || texts.errorTitle;
    errorText.textContent = text;
    errorBox.hidden = false;
  };

  const clearError = () => {
    errorBox.hidden = true;
  };

  // One paragraph per line of text: a text is never read as HTML.
  const setText = (box, text) => {
    box.replaceChildren(...text.split('\n').filter((line) => line.trim() !== '').map((line) => {
      const paragraph = document.createElement('p');
      paragraph.textContent = line;
      return paragraph;
    }));
  };

  // The conversation scrolls inside its own window (PAPER-42). It follows the end of the talk while the reader is at the
  // end; a reader who went up to read is left alone, and a button takes them back to the latest message.
  let follow = true;
  const atEnd = () => log.scrollHeight - log.scrollTop - log.clientHeight < 48;
  const reveal = () => {
    if (follow) {
      log.scrollTop = log.scrollHeight;
    }

    jump.hidden = follow;
  };

  log.addEventListener('scroll', () => {
    follow = atEnd();
    jump.hidden = follow;
  });

  jump.textContent = texts.jump;
  jump.addEventListener('click', () => {
    follow = true;
    reveal();
    field.focus();
  });

  const addMessage = (who, text, kind, pending) => {
    const item = document.createElement('li');
    item.className = `message message--${kind}${pending ? ' message--pending' : ''}`;
    const name = document.createElement('span');
    name.className = 'message__who';
    name.textContent = who;
    const box = document.createElement('div');
    box.className = 'message__text';
    setText(box, text);
    item.append(name, box);
    log.append(item);
    reveal();
    return { item, box };
  };

  // Reads a streamed NDJSON response and calls onEvent for each object as soon as its line is complete. The network delivers
  // bytes in chunks that do not follow the lines (or even the letters): the incomplete end is kept for the next chunk.
  const readLines = async (stream, onEvent) => {
    const reader = stream.getReader();
    const decoder = new TextDecoder('utf-8');
    let pending = '';
    while (true) {
      const { value, done } = await reader.read();
      pending += done ? decoder.decode() : decoder.decode(value, { stream: true });
      const lines = pending.split('\n');
      pending = done ? '' : lines.pop();
      for (const line of lines) {
        if (line.trim() !== '') {
          onEvent(JSON.parse(line));
        }
      }

      if (done) {
        return;
      }
    }
  };

  const send = async (text) => {
    busy = true;
    follow = true; // Sending is going back to the talk.
    sendButton.disabled = true;
    clearError();
    messages.push({ role: 'user', content: text });
    const mine = addMessage(texts.you, text, 'user', false);
    const theirs = addMessage(texts.name, texts.writing, 'character', true);
    let reply = '';
    let failure = null;
    let complete = false;

    try {
      const response = await fetch(root.dataset.endpoint, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json', RequestVerificationToken: token },
        body: JSON.stringify({ messages }),
      });
      if (!response.ok || !response.body) {
        failure = response.status === 404 ? 'notFound' : response.status === 409 ? 'noModel' : 'refused';
      } else {
        await readLines(response.body, (event) => {
          if (event.type === 'text') {
            reply += event.text;
            theirs.item.classList.remove('message--pending');
            setText(theirs.box, reply.trimStart());
            reveal();
          } else if (event.type === 'error') {
            failure = event.code;
          } else if (event.type === 'done') {
            complete = true;
          }
        });
      }
    } catch {
      failure = 'network';
    }

    if (!failure && (!complete || reply.trim() === '')) {
      failure = complete ? 'invalidReply' : 'network';
    }

    if (failure) {
      // The message did not get an answer: it is taken back, to be sent again, so the conversation stays consistent.
      theirs.item.remove();
      mine.item.remove();
      messages.pop();
      if (field.value.trim() === '') {
        field.value = text;
      }

      showError(texts.errors[failure] || texts.errors.unexpected);
    } else {
      messages.push({ role: 'assistant', content: reply.trim() });
      reveal(); // The box has stopped growing: bring its end into view.
    }

    // One more exchange would go past the limit of the conversation: it is over.
    if (messages.length + 2 > texts.maxMessages) {
      field.disabled = true;
      sendButton.disabled = true;
      showError(texts.limit, texts.limitTitle);
    } else {
      sendButton.disabled = false;
      field.focus();
    }

    busy = false;
  };

  form.addEventListener('submit', (event) => {
    event.preventDefault();
    const text = field.value.trim();
    if (busy || text === '') {
      return;
    }

    field.value = '';
    send(text);
  });

  // Enter sends the message; Shift+Enter starts a new line. Not while an accent or an ideogram is being composed.
  field.addEventListener('keydown', (event) => {
    if (event.key === 'Enter' && !event.shiftKey && !event.isComposing) {
      event.preventDefault();
      form.requestSubmit();
    }
  });
})();
