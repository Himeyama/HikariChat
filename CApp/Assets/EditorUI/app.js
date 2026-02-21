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

// Settings functionality
const settingsButton = document.getElementById("settingsButton");

const SETTINGS_KEY = "chatSettings";

const defaultSettings = {
    apiType: "chat_completions",
    endpointPreset: "openai",
    apiEndpoint: "https://api.openai.com/v1/chat/completions",
    apiKey: "",
    model: "gpt-4o-mini",
    azureDeployment: "",
    streaming: true
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
    const activeTab = getActiveTab();
    if (activeTab) {
        updateSendButtonState(activeTab);
    }
}

function openSettingsWindow() {
    // C# 側に設定画面を開くメッセージを送信
    window.chrome.webview.postMessage('{ "method": "tools/call", "params": {"name": "openSettings", "arguments": {} } }');
}

settingsButton.addEventListener("click", openSettingsWindow);

// Tab management
let tabCounter = 1;
let tabs = {}; // { tabId: { conversationHistory, isLoading } }
let activeTabId = "tab-chat-1";

// 最初のタブを初期化
tabs["tab-chat-1"] = {
    conversationHistory: [],
    isLoading: false
};

function getActiveTab() {
    return tabs[activeTabId];
}

function getActiveTabElements() {
    // tabId から番号を抽出 (例: "tab-chat-12" -> "12")
    const tabNum = activeTabId.replace('tab-chat-', '');
    return {
        chatMessages: document.getElementById(`chatMessages-${tabNum}`),
        chatInput: document.getElementById(`chatInput-${tabNum}`),
        sendButton: document.getElementById(`sendButton-${tabNum}`)
    };
}

// タブ ID から番号を取得
function getTabNumber(tabId) {
    return tabId.replace('tab-chat-', '');
}

// 新しいタブを追加
function addNewTab() {
    tabCounter++;
    const tabId = `tab-chat-${tabCounter}`;
    const tabNum = tabCounter;

    // タブデータを初期化
    tabs[tabId] = {
        conversationHistory: [],
        isLoading: false
    };

    // タブボタンを追加
    const tabList = document.querySelector('.chat-tabs menu[role="tablist"]');
    const tabButton = document.createElement("button");
    tabButton.setAttribute("role", "tab");
    tabButton.setAttribute("aria-selected", "false");
    tabButton.setAttribute("aria-controls", tabId);
    tabButton.setAttribute("id", `${tabId}-btn`);
    tabButton.textContent = `チャット ${tabNum}`;
    tabButton.addEventListener("click", () => switchTab(tabId));
    tabList.appendChild(tabButton);

    // タブパネルを追加（他のパネルの後に追加）
    const tabPanel = document.createElement("article");
    tabPanel.setAttribute("role", "tabpanel");
    tabPanel.setAttribute("id", tabId);
    tabPanel.setAttribute("hidden", "true");
    tabPanel.innerHTML = `
        <div class="chat-messages" id="chatMessages-${tabNum}"></div>
        <div class="chat-input-area">
            <textarea id="chatInput-${tabNum}" placeholder="メッセージを入力..." rows="3"></textarea>
            <button id="sendButton-${tabNum}" class="send-button">送信</button>
        </div>
    `;
    document.querySelector('.chat-tabs').appendChild(tabPanel);
    
    // 新しいタブに切り替え
    switchTab(tabId);
    
    // 入力イベントをバインド
    bindInputEvents(tabNum);
}

// タブを切り替え
function switchTab(tabId) {
    // 現在のタブの送信ボタン状態を保存
    const currentElems = getActiveTabElements();
    
    // すべてのタブの選択状態を解除
    document.querySelectorAll('.chat-tabs menu[role="tablist"] button[role="tab"]').forEach(t => {
        t.setAttribute("aria-selected", "false");
    });
    
    // すべてのパネルを非表示
    document.querySelectorAll('.chat-tabs article[role="tabpanel"]').forEach(panel => {
        panel.setAttribute("hidden", "true");
    });
    
    // 選択したタブをアクティブに
    document.getElementById(`${tabId}-btn`).setAttribute("aria-selected", "true");
    document.getElementById(tabId).removeAttribute("hidden");
    
    activeTabId = tabId;
    
    // 新しいタブの送信ボタン状態を更新
    const newElems = getActiveTabElements();
    updateSendButtonState();
    newElems.chatInput.focus();
}

// 送信ボタンの状態を更新
function updateSendButtonState() {
    const elems = getActiveTabElements();
    const tab = getActiveTab();
    const message = elems.chatInput.value.trim();
    const hasApiKey = currentSettings.apiKey || currentSettings.endpointPreset === "ollama";
    elems.sendButton.disabled = !message || tab.isLoading || !hasApiKey;
}

// 入力イベントをバインド
function bindInputEvents(tabNum) {
    const chatInput = document.getElementById(`chatInput-${tabNum}`);
    const sendButton = document.getElementById(`sendButton-${tabNum}`);
    
    chatInput.addEventListener("input", updateSendButtonState);
    
    chatInput.addEventListener("keydown", (e) => {
        if (e.key === "Enter" && !e.shiftKey) {
            e.preventDefault();
            sendMessage();
        }
    });
    
    sendButton.addEventListener("click", sendMessage);
}

// チャットをリセット
function resetChat() {
    const tab = getActiveTab();
    const elems = getActiveTabElements();
    tab.conversationHistory = [];
    elems.chatMessages.innerHTML = "";
    updateSendButtonState();
    elems.chatInput.focus();
}

const resetButton = document.getElementById("resetButton");
resetButton.addEventListener("click", resetChat);

// タブ追加ボタン
const addTabButton = document.getElementById("addTabButton");
addTabButton.addEventListener("click", addNewTab);

// メッセージを追加
function addMessage(content, role) {
    const elems = getActiveTabElements();
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

    elems.chatMessages.appendChild(messageDiv);
    elems.chatMessages.scrollTop = elems.chatMessages.scrollHeight;
}

let isLoading = false;

async function sendMessage() {
    const elems = getActiveTabElements();
    const tab = getActiveTab();
    const message = elems.chatInput.value.trim();
    if (!message || tab.isLoading) return;

    console.log('[Send Message] currentSettings:', currentSettings);

    if (!currentSettings.apiKey && currentSettings.endpointPreset !== "ollama") {
        addMessage("API キーが設定されていません。設定から入力してください。", "error");
        openSettingsWindow();
        return;
    }

    tab.isLoading = true;
    updateSendButtonState();

    // ユーザーメッセージを表示
    addMessage(message, "user");
    tab.conversationHistory.push({ role: "user", content: message });
    elems.chatInput.value = "";

    // ストリーミング用のプレースホルダーメッセージを作成
    let assistantMessageDiv = null;
    let assistantContent = "";

    try {
        if (currentSettings.streaming) {
            // ストリーミング処理
            assistantMessageDiv = createAssistantMessageDiv();

            const response = await fetch("/api/chat", {
                method: "POST",
                headers: {
                    "Content-Type": "application/json"
                },
                body: JSON.stringify({
                    messages: tab.conversationHistory,
                    apiKey: currentSettings.apiKey,
                    apiEndpoint: currentSettings.apiEndpoint,
                    model: currentSettings.model,
                    apiType: currentSettings.apiType,
                    endpointPreset: currentSettings.endpointPreset,
                    azureDeployment: currentSettings.azureDeployment,
                    streaming: true
                })
            });

            if (!response.ok) {
                const errorData = await response.json();
                throw new Error(errorData.error || "API エラーが発生しました");
            }

            const reader = response.body.getReader();
            const decoder = new TextDecoder();
            let buffer = "";

            while (true) {
                const { done, value } = await reader.read();
                if (done) break;

                buffer += decoder.decode(value, { stream: true });
                const lines = buffer.split("\n");
                buffer = lines.pop() || "";

                for (const line of lines) {
                    if (line.startsWith("data: ")) {
                        const data = line.substring(6);
                        if (data === "[DONE]") continue;

                        try {
                            const parsed = JSON.parse(data);
                            let delta = "";

                            if (parsed.choices && parsed.choices[0]?.delta?.content) {
                                delta = parsed.choices[0].delta.content;
                            } else if (parsed.content && Array.isArray(parsed.content)) {
                                // Anthropic 形式
                                if (parsed.content[0]?.text) {
                                    delta = parsed.content[0].text;
                                }
                            }

                            if (delta) {
                                assistantContent += delta;
                                updateAssistantMessage(assistantMessageDiv, assistantContent);
                            }
                        } catch (e) {
                            // JSON パースエラーは無視
                        }
                    }
                }
            }

            // 会話履歴に追加
            tab.conversationHistory.push({ role: "assistant", content: assistantContent });
        } else {
            // 通常処理
            const response = await fetch("/api/chat", {
                method: "POST",
                headers: {
                    "Content-Type": "application/json"
                },
                body: JSON.stringify({
                    messages: tab.conversationHistory,
                    apiKey: currentSettings.apiKey,
                    apiEndpoint: currentSettings.apiEndpoint,
                    model: currentSettings.model,
                    apiType: currentSettings.apiType,
                    endpointPreset: currentSettings.endpointPreset,
                    azureDeployment: currentSettings.azureDeployment,
                    streaming: false
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
                tab.conversationHistory.push({ role: "assistant", content: assistantMessage });
            } else {
                throw new Error("空のレスポンスが返されました");
            }
        }
    } catch (error) {
        addMessage(error.message, "error");
        console.error("Chat error:", error);
    } finally {
        tab.isLoading = false;
        updateSendButtonState();
        elems.chatInput.focus();
    }
}

// アシスタントメッセージ用の div を作成
function createAssistantMessageDiv() {
    const elems = getActiveTabElements();
    const messageDiv = document.createElement("div");
    messageDiv.className = "chat-message assistant";

    const roleSpan = document.createElement("div");
    roleSpan.className = "role";
    roleSpan.textContent = "AI";
    messageDiv.appendChild(roleSpan);

    const contentDiv = document.createElement("div");
    contentDiv.className = "message-content";
    messageDiv.appendChild(contentDiv);

    elems.chatMessages.appendChild(messageDiv);
    elems.chatMessages.scrollTop = elems.chatMessages.scrollHeight;

    return contentDiv;
}

// アシスタントメッセージを更新
function updateAssistantMessage(contentDiv, content) {
    contentDiv.innerHTML = marked.parse(content);
    const elems = getActiveTabElements();
    elems.chatMessages.scrollTop = elems.chatMessages.scrollHeight;
}

// 初期状態を設定
updateSendButtonState();

// 最初のタブの入力イベントをバインド
bindInputEvents(1);

// 設定更新通知を受信
window.chrome.webview.addEventListener("message", (e) => {
    if (e.data === "settingsUpdated") {
        refreshSettings();
    }
});

// タブ切り替え機能
const tabButtons = document.querySelectorAll('[role="tab"]');
const tabPanels = document.querySelectorAll('[role="tabpanel"]');

tabButtons.forEach(tab => {
    tab.addEventListener("click", () => {
        const tabId = tab.getAttribute("aria-controls");
        switchTab(tabId);
    });
});
