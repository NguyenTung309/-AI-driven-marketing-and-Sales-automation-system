const FOCUSABLE_SELECTOR = [
  "a[href]",
  "button:not([disabled])",
  "input:not([disabled]):not([type='hidden'])",
  "select:not([disabled])",
  "textarea:not([disabled])",
  "[contenteditable='true']",
  "[tabindex]:not([tabindex='-1'])",
].join(",");

let activeDialogs: readonly HTMLElement[] = [];

function isVisible(element: HTMLElement): boolean {
  return element.getClientRects().length > 0
    && window.getComputedStyle(element).visibility !== "hidden";
}

function isActiveRadioInGroup(element: HTMLElement, container: HTMLElement): boolean {
  if (!(element instanceof HTMLInputElement) || element.type !== "radio" || !element.name) return true;

  const group = Array.from(container.querySelectorAll<HTMLInputElement>('input[type="radio"]')).filter(
    (candidate) => candidate.name === element.name
      && candidate.form === element.form
      && candidate.closest('[role="radiogroup"]') === element.closest('[role="radiogroup"]')
      && !candidate.disabled
      && isVisible(candidate),
  );
  const checked = group.find((candidate) => candidate.checked);
  return element === (checked ?? group[0]);
}

function isTabbable(element: HTMLElement, container: HTMLElement): boolean {
  return element.tabIndex >= 0
    && !element.matches(":disabled")
    && !element.closest("[inert]")
    && isVisible(element)
    && isActiveRadioInGroup(element, container);
}

export function getFocusableElements(container: HTMLElement): readonly HTMLElement[] {
  return Array.from(container.querySelectorAll<HTMLElement>(FOCUSABLE_SELECTOR)).filter(
    (element) => isTabbable(element, container),
  );
}

export function focusInitialElement(dialog: HTMLElement): void {
  const focusableElements = getFocusableElements(dialog);
  const preferred = dialog.querySelector<HTMLElement>("[data-modal-initial-focus], [autofocus]");
  const target = preferred && focusableElements.includes(preferred)
    ? preferred
    : focusableElements[0] ?? dialog;
  target.focus();
}

export function containTabFocus(event: KeyboardEvent, dialog: HTMLElement): void {
  if (event.key !== "Tab") return;

  const focusableElements = getFocusableElements(dialog);
  if (focusableElements.length === 0) {
    event.preventDefault();
    dialog.focus();
    return;
  }

  const first = focusableElements[0];
  const last = focusableElements[focusableElements.length - 1];
  const activeElement = document.activeElement;

  if (!dialog.contains(activeElement)) {
    event.preventDefault();
    (event.shiftKey ? last : first).focus();
    return;
  }

  if (event.shiftKey && activeElement === first) {
    event.preventDefault();
    last.focus();
    return;
  }

  if (!event.shiftKey && activeElement === last) {
    event.preventDefault();
    first.focus();
  }
}

export function registerActiveDialog(dialog: HTMLElement): () => void {
  activeDialogs = [...activeDialogs.filter((candidate) => candidate !== dialog), dialog];
  return () => {
    activeDialogs = activeDialogs.filter((candidate) => candidate !== dialog);
  };
}

export function isTopmostDialog(dialog: HTMLElement | null): boolean {
  return dialog !== null && activeDialogs[activeDialogs.length - 1] === dialog;
}

export function canRestoreFocus(opener: HTMLElement): boolean {
  const topmostDialog = activeDialogs[activeDialogs.length - 1];
  return !topmostDialog || topmostDialog.contains(opener);
}
