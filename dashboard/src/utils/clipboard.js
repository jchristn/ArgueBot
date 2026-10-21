function legacyCopy(text) {
  const focus = document.activeElement;
  const selection = document.getSelection();
  const ranges = selection ? Array.from({ length: selection.rangeCount }, (_, index) => selection.getRangeAt(index).cloneRange()) : [];
  const inputSelection = focus && typeof focus.selectionStart === "number" ? [focus.selectionStart, focus.selectionEnd] : null;
  const field = document.createElement("textarea");
  field.value = text;
  field.readOnly = true;
  field.style.cssText = "position:fixed;top:0;left:0;width:1px;height:1px;opacity:0;";
  // A native dialog makes elements outside it inert, including temporary copy fields.
  (Array.from(document.querySelectorAll("dialog[open]")).at(-1) || document.body).append(field);
  try {
    field.focus({ preventScroll: true });
    field.select();
    return typeof document.execCommand === "function" && document.execCommand("copy");
  } catch { return false; }
  finally {
    field.remove();
    if (focus?.isConnected) focus.focus({ preventScroll: true });
    if (inputSelection) focus.setSelectionRange(...inputSelection);
    else if (selection) { selection.removeAllRanges(); ranges.forEach(range => selection.addRange(range)); }
  }
}

/** Copy on HTTPS, loopback and insecure HTTP. False means the UI must offer manual copying. */
export async function copyText(text) {
  if (navigator.clipboard?.writeText) {
    try { await navigator.clipboard.writeText(text); return true; } catch { /* Permission or browser policy: use the selected-text fallback. */ }
  }
  return legacyCopy(text);
}
