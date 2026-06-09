// Scroll automático al último mensaje
window.scrollToBottom = (elementId) => {
    const el = document.getElementById(elementId);
    if (el) el.scrollTop = el.scrollHeight;
};

// Auto-resize del textarea según contenido
window.autoResizeTextarea = (el) => {
    if (!el) return;
    el.style.height = 'auto';
    el.style.height = Math.min(el.scrollHeight, 140) + 'px';
};
