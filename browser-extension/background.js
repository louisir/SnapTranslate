const ENDPOINT = "http://127.0.0.1:49387/selection";

chrome.runtime.onMessage.addListener((message, sender, sendResponse) => {
  if (!message || message.type !== "snaptranslate.selection") {
    return false;
  }

  fetch(ENDPOINT, {
    method: "POST",
    headers: {
      "Content-Type": "application/json"
    },
    body: JSON.stringify({
      text: message.text,
      screenX: message.screenX,
      screenY: message.screenY,
      rectLeft: message.rectLeft,
      rectTop: message.rectTop,
      rectRight: message.rectRight,
      rectBottom: message.rectBottom
    })
  })
    .then((response) => {
      sendResponse({ ok: response.ok });
    })
    .catch((error) => {
      sendResponse({ ok: false, error: String(error) });
    });

  return true;
});
