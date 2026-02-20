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

// Chat functionality
const chatMessages = document.getElementById("chatMessages");
const chatInput = document.getElementById("chatInput");
const sendButton = document.getElementById("sendButton");
const resetButton = document.getElementById("resetButton");

// Settings functionality
const settingsButton = document.getElementById("settingsButton");

const SETTINGS_KEY = "chatSettings";

const defaultSettings = {
    apiType: "chat_completions",
    endpointPreset: "openai",
    apiEndpoint: "https://api.openai.com/v1/chat/completions",
    apiKey: "",
    model: "gpt-4o-mini",
    azureDeployment: ""
};

function loadSettings() {
    const saved = localStorage.getItem(SETTINGS_KEY);
    if (saved) {
        try {
            return { ...defaultSettings, ...JSON.parse(saved) };
        } catch {
            return defaultSettings;
        }
    }
    return defaultSettings;
}

function saveSettingsToStorage(settings) {
    localStorage.setItem(SETTINGS_KEY, JSON.stringify(settings));
}

let currentSettings = loadSettings();

// 設定を再読み込み
function refreshSettings() {
    currentSettings = loadSettings();
    console.log('[Settings Updated]', currentSettings);
    updateSendButtonState();
}

// 送信ボタンの状態を更新
function updateSendButtonState() {
    const message = chatInput.value.trim();
    const hasApiKey = currentSettings.apiKey || currentSettings.endpointPreset === "ollama";
    sendButton.disabled = !message || isLoading || !hasApiKey;
}

function openSettingsWindow() {
    // C# 側に設定画面を開くメッセージを送信
    window.chrome.webview.postMessage('{ "method": "tools/call", "params": {"name": "openSettings", "arguments": {} } }');
}

settingsButton.addEventListener("click", openSettingsWindow);

let conversationHistory = [];
let isLoading = false;

function addMessage(content, role) {
    const messageDiv = document.createElement("div");
    messageDiv.className = `chat-message ${role}`;

    const roleSpan = document.createElement("div");
    roleSpan.className = "role";
    roleSpan.textContent = role === "user" ? "あなた" : (role === "assistant" ? "AI" : "エラー");
    messageDiv.appendChild(roleSpan);

    const contentDiv = document.createElement("div");
    contentDiv.className = "message-content";

    // Markdown としてレンダリング（ユーザーメッセージはプレーンテキスト）
    if (role === "assistant") {
        contentDiv.innerHTML = marked.parse(content);
    } else {
        contentDiv.textContent = content;
    }

    messageDiv.appendChild(contentDiv);

    chatMessages.appendChild(messageDiv);
    chatMessages.scrollTop = chatMessages.scrollHeight;
}

// チャットをリセット
function resetChat() {
    conversationHistory = [];
    chatMessages.innerHTML = "";
    updateSendButtonState();
    chatInput.focus();
}

resetButton.addEventListener("click", resetChat);

async function sendMessage() {
    const message = chatInput.value.trim();
    if (!message || isLoading) return;

    console.log('[Send Message] currentSettings:', currentSettings);

    if (!currentSettings.apiKey && currentSettings.endpointPreset !== "ollama") {
        addMessage("API キーが設定されていません。設定から入力してください。", "error");
        openSettingsWindow();
        return;
    }

    isLoading = true;
    sendButton.disabled = true;

    // ユーザーメッセージを表示
    addMessage(message, "user");
    conversationHistory.push({ role: "user", content: message });
    chatInput.value = "";

    try {
        const response = await fetch("/api/chat", {
            method: "POST",
            headers: {
                "Content-Type": "application/json"
            },
            body: JSON.stringify({
                messages: conversationHistory,
                apiKey: currentSettings.apiKey,
                apiEndpoint: currentSettings.apiEndpoint,
                model: currentSettings.model,
                apiType: currentSettings.apiType,
                endpointPreset: currentSettings.endpointPreset,
                azureDeployment: currentSettings.azureDeployment
            })
        });

        if (!response.ok) {
            const errorData = await response.json();
            throw new Error(errorData.error || "API エラーが発生しました");
        }

        const data = await response.json();
        let assistantMessage = "";

        // API 種別に応じてレスポンスを処理
        if (currentSettings.apiType === "gemini") {
            assistantMessage = data.candidates?.[0]?.content?.parts?.[0]?.text || "";
        } else if (currentSettings.apiType === "anthropic") {
            assistantMessage = data.content?.[0]?.text || "";
        } else {
            assistantMessage = data.choices?.[0]?.message?.content || "";
        }

        if (assistantMessage) {
            addMessage(assistantMessage, "assistant");
            conversationHistory.push({ role: "assistant", content: assistantMessage });
        } else {
            throw new Error("空のレスポンスが返されました");
        }
    } catch (error) {
        addMessage(error.message, "error");
        console.error("Chat error:", error);
    } finally {
        isLoading = false;
        updateSendButtonState();
        chatInput.focus();
    }
}

sendButton.addEventListener("click", sendMessage);

chatInput.addEventListener("input", updateSendButtonState);

chatInput.addEventListener("keydown", (e) => {
    if (e.key === "Enter" && !e.shiftKey) {
        e.preventDefault();
        sendMessage();
    }
});

// 初期状態を設定
updateSendButtonState();

// 設定更新通知を受信
window.chrome.webview.addEventListener("message", (e) => {
    if (e.data === "settingsUpdated") {
        refreshSettings();
    }
});
