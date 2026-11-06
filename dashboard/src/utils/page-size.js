/** Rows-per-page choices offered by every paged table. */
export const PAGE_SIZES = [10, 25, 50, 100];

const PREFIX = "arguebot.pageSize.";

/** The rows-per-page choice saved for a table, or the fallback when none is saved or storage is unavailable. */
export function savedPageSize(table, fallback) {
  try {
    const value = Number(localStorage.getItem(PREFIX + table));
    return PAGE_SIZES.includes(value) ? value : fallback;
  } catch {
    return fallback;
  }
}

/** Remember a table's rows-per-page choice for later visits. Storage failures are ignored; the choice still applies now. */
export function savePageSize(table, size) {
  if (!table || !PAGE_SIZES.includes(size)) return;
  try { localStorage.setItem(PREFIX + table, String(size)); } catch { /* Private browsing or a full quota keeps the default next time. */ }
}
