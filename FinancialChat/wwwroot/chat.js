// Scroll automático al último mensaje
window.scrollToBottom = (elementId) => {
    const el = document.getElementById(elementId);
    if (el) el.scrollTop = el.scrollHeight;
};
