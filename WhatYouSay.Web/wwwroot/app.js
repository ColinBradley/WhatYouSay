/**
 * Copies a readonly field the page only ever shows once, so the value never has to be
 * selected by hand off a page that will not show it again.
 *
 * @param {HTMLTextAreaElement} source The field to copy from.
 * @param {HTMLElement} button The control that was clicked, relabelled to confirm.
 * @returns {Promise<void>}
 */
window.whatYouSayCopy = async (source, button) => {
    try {
        await navigator.clipboard.writeText(source.value);
    } catch {
        // Needs a secure context, which plain http on a LAN is not. Select it instead so
        // the keyboard still works.
        source.select();

        return;
    }

    const originalButtonTextContent = button.textContent;

    button.textContent = 'Copied';
    setTimeout(() => button.textContent = originalButtonTextContent, 2000);
};
