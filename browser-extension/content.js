const MAX_SELECTION_LENGTH = 4000;
const SETTLE_DELAY_MS = 60;

let lastSentKey = "";
let lastSentAt = 0;
let pendingTimer = 0;

document.addEventListener("mouseup", (event) => {
  queueSelection(event);
}, true);

document.addEventListener("dblclick", (event) => {
  queueSelection(event);
}, true);

document.addEventListener("keyup", (event) => {
  if (event.key === "Shift" || event.key.startsWith("Arrow")) {
    queueSelection(event);
  }
}, true);

function queueSelection(event) {
  window.clearTimeout(pendingTimer);
  pendingTimer = window.setTimeout(() => sendSelection(event), SETTLE_DELAY_MS);
}

function sendSelection(event) {
  const selection = window.getSelection();
  if (!selection || selection.isCollapsed) {
    return;
  }

  const text = normalizeText(selection.toString());
  if (!isUsefulText(text)) {
    return;
  }

  const point = getScreenPoint(event, selection);
  const key = `${text}\n${point.screenX},${point.screenY}`;
  const now = Date.now();
  if (key === lastSentKey && now - lastSentAt < 1200) {
    return;
  }

  lastSentKey = key;
  lastSentAt = now;

  chrome.runtime.sendMessage({
    type: "snaptranslate.selection",
    text,
    screenX: point.screenX,
    screenY: point.screenY
  });
}

function normalizeText(value) {
  return value
    .normalize("NFKC")
    .replace(/\u00a0/g, " ")
    .replace(/[\u200b\u200c\u200d\ufeff]/g, "")
    .replace(/\s+/g, " ")
    .trim()
    .slice(0, MAX_SELECTION_LENGTH);
}

function isUsefulText(value) {
  return /[\p{L}\p{N}\u4e00-\u9fff]/u.test(value);
}

function getScreenPoint(event, selection) {
  if (Number.isFinite(event.screenX) && Number.isFinite(event.screenY)) {
    return {
      screenX: Math.round(event.screenX),
      screenY: Math.round(event.screenY)
    };
  }

  const rect = getSelectionRect(selection);
  if (rect) {
    return {
      screenX: Math.round(window.screenX + rect.left),
      screenY: Math.round(window.screenY + rect.top)
    };
  }

  return {
    screenX: Math.round(window.screenX),
    screenY: Math.round(window.screenY)
  };
}

function getSelectionRect(selection) {
  for (let index = 0; index < selection.rangeCount; index += 1) {
    const range = selection.getRangeAt(index);
    const rects = Array.from(range.getClientRects()).filter((rect) => rect.width > 0 && rect.height > 0);
    if (rects.length > 0) {
      return rects[rects.length - 1];
    }
  }

  return null;
}
