const MAX_SELECTION_LENGTH = 4000;
const SETTLE_DELAY_MS = 160;

let lastSentKey = "";
let lastSentAt = 0;
let pendingTimer = 0;
let pointerDown = false;
let lastPointerEvent = null;
let selectionDirty = false;

document.addEventListener("pointerdown", (event) => {
  pointerDown = true;
  lastPointerEvent = event;
}, true);

document.addEventListener("pointermove", (event) => {
  if (pointerDown) {
    lastPointerEvent = event;
  }
}, true);

document.addEventListener("pointerup", (event) => {
  pointerDown = false;
  lastPointerEvent = event;
  queueSelection(event, 90);
}, true);

document.addEventListener("dblclick", (event) => {
  pointerDown = false;
  lastPointerEvent = event;
  queueSelection(event, 120);
}, true);

document.addEventListener("selectionchange", () => {
  selectionDirty = true;
  if (!pointerDown) {
    queueSelection(lastPointerEvent, SETTLE_DELAY_MS);
  }
}, true);

document.addEventListener("select", (event) => {
  lastPointerEvent = lastPointerEvent ?? event;
  queueSelection(lastPointerEvent, 80);
}, true);

document.addEventListener("keyup", (event) => {
  if (event.key === "Shift" || event.key.startsWith("Arrow") || event.ctrlKey || event.metaKey) {
    lastPointerEvent = event;
    queueSelection(event, SETTLE_DELAY_MS);
  }
}, true);

function queueSelection(event, delayMs) {
  window.clearTimeout(pendingTimer);
  pendingTimer = window.setTimeout(() => sendSelection(event), delayMs);
}

function sendSelection(event) {
  if (pointerDown) {
    selectionDirty = true;
    return;
  }

  const snapshot = getSelectionSnapshot(event);
  if (!snapshot) {
    return;
  }

  const text = normalizeText(snapshot.text);
  if (!isUsefulText(text)) {
    return;
  }

  const point = getScreenPoint(event, snapshot.rect);
  const screenRect = getScreenRect(event, snapshot.rect);
  const key = `${text}\n${point.screenX},${point.screenY}`;
  const now = Date.now();
  if (!selectionDirty && key === lastSentKey && now - lastSentAt < 1200) {
    return;
  }

  selectionDirty = false;
  lastSentKey = key;
  lastSentAt = now;

  chrome.runtime.sendMessage({
    type: "snaptranslate.selection",
    text,
    screenX: point.screenX,
    screenY: point.screenY,
    rectLeft: screenRect?.left ?? null,
    rectTop: screenRect?.top ?? null,
    rectRight: screenRect?.right ?? null,
    rectBottom: screenRect?.bottom ?? null
  });
}

function getSelectionSnapshot(event) {
  const controlSelection = getControlSelectionSnapshot(event);
  if (controlSelection) {
    return controlSelection;
  }

  const selection = window.getSelection();
  if (!selection || selection.isCollapsed) {
    return null;
  }

  return {
    text: selection.toString(),
    rect: getSelectionRect(selection)
  };
}

function getControlSelectionSnapshot(event) {
  const element = getSelectionControl(event?.target) ?? getSelectionControl(document.activeElement);
  if (!element) {
    return null;
  }

  const start = element.selectionStart;
  const end = element.selectionEnd;
  if (!Number.isInteger(start) || !Number.isInteger(end) || start === end) {
    return null;
  }

  return {
    text: element.value.slice(Math.min(start, end), Math.max(start, end)),
    rect: element.getBoundingClientRect()
  };
}

function getSelectionControl(element) {
  if (!element || element.nodeType !== Node.ELEMENT_NODE) {
    return null;
  }

  if (element instanceof HTMLTextAreaElement) {
    return element;
  }

  if (!(element instanceof HTMLInputElement)) {
    return null;
  }

  const supportedTypes = new Set(["", "text", "search", "url", "tel", "email", "password"]);
  return supportedTypes.has(element.type) ? element : null;
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

function getScreenPoint(event, selectionRect) {
  if (Number.isFinite(event?.screenX) && Number.isFinite(event?.screenY)) {
    return {
      screenX: Math.round(event.screenX),
      screenY: Math.round(event.screenY)
    };
  }

  const screenRect = getScreenRect(event, selectionRect);
  if (screenRect) {
    return {
      screenX: Math.round(screenRect.right),
      screenY: Math.round(screenRect.bottom)
    };
  }

  return {
    screenX: Math.round(window.screenX),
    screenY: Math.round(window.screenY)
  };
}

function getScreenRect(event, rect) {
  if (!rect) {
    return null;
  }

  const hasMouseCoordinates =
    Number.isFinite(event?.screenX) &&
    Number.isFinite(event?.screenY) &&
    Number.isFinite(event?.clientX) &&
    Number.isFinite(event?.clientY);
  const offsetX = hasMouseCoordinates ? event.screenX - event.clientX : window.screenX;
  const offsetY = hasMouseCoordinates ? event.screenY - event.clientY : window.screenY;

  return {
    left: Math.round(offsetX + rect.left),
    top: Math.round(offsetY + rect.top),
    right: Math.round(offsetX + rect.right),
    bottom: Math.round(offsetY + rect.bottom)
  };
}

function getSelectionRect(selection) {
  let firstRect = null;
  let lastRect = null;
  for (let index = 0; index < selection.rangeCount; index += 1) {
    const range = selection.getRangeAt(index);
    const rects = Array.from(range.getClientRects()).filter((rect) => rect.width > 0 && rect.height > 0);
    if (rects.length === 0) {
      continue;
    }

    firstRect = firstRect ?? rects[0];
    lastRect = rects[rects.length - 1];
  }

  return lastRect ?? firstRect;
}
