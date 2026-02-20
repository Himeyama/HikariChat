const toggleMaximizeCommand = () => {
    window.chrome.webview.postMessage("{ \"method\": \"tools/call\", \"params\": {\"name\": \"control\", \"arguments\": {\"command\": \"toggleMaximize\" }} }");
}

const minimizeCommand = () => {
    window.chrome.webview.postMessage("{ \"method\": \"tools/call\", \"params\": {\"name\": \"control\", \"arguments\": {\"command\": \"minimize\" }} }");
}

const closeCommand = () => {
    window.chrome.webview.postMessage("{ \"method\": \"tools/call\", \"params\": {\"name\": \"control\", \"arguments\": {\"command\": \"close\" }} }");
}

// 要素取得
const btnMin = document.getElementById("Minimize");
const btnMax = document.getElementById("Maximize");
const btnRestore = document.getElementById("Restore");
const btnClose = document.getElementById("Close");

let isMaximized = false;
const toggleMaximize = () => {
    isMaximized = !isMaximized;
    if (isMaximized) {
        btnMax.classList.add("hidden");
        btnRestore.classList.remove("hidden");
        toggleMaximizeCommand()
    } else {
        btnMax.classList.remove("hidden");
        btnRestore.classList.add("hidden");
        toggleMaximizeCommand();
    }
}

btnMin.addEventListener("click", () => minimizeCommand());
btnMax.addEventListener("click", () => {
    toggleMaximize();
});
btnRestore.addEventListener("click", () => {
    toggleMaximize();
});
btnClose.addEventListener("click", () => closeCommand());