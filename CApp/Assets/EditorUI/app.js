const toggleMaximizeCommand = () => {
    window.chrome.webview.postMessage("{ \"method\": \"tools/call\", \"params\": {\"name\": \"control\", \"arguments\": {\"command\": \"toggleMaximize\" }} }");
}

const minimizeCommand = () => {
    window.chrome.webview.postMessage("{ \"method\": \"tools/call\", \"params\": {\"name\": \"control\", \"arguments\": {\"command\": \"minimize\" }} }");
}

const closeCommand = () => {
    window.chrome.webview.postMessage("{ \"method\": \"tools/call\", \"params\": {\"name\": \"control\", \"arguments\": {\"command\": \"close\" }} }");
}

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

function updateModelDisplay() {
    const modelDisplay = document.getElementById('modelDisplay');
    if (!modelDisplay) return;
    
    const model = currentSettings.model || 'モデル未設定';
    const endpointPreset = currentSettings.endpointPreset || 'unknown';
    
    const presetNames = {
        openai: 'OpenAI',
        azure_openai: 'Azure OpenAI',
        gemini: 'Google Gemini',
        grok: 'Grok (xAI)',
        anthropic: 'Anthropic',
        ollama: 'Ollama',
        custom: 'Custom'
    };
    
    const providerName = presetNames[endpointPreset] || endpointPreset;
    modelDisplay.textContent = `${providerName} / ${model}`;
}

function refreshSettings() {
    currentSettings = loadSettings();
    console.log('[Settings Updated]', currentSettings);
    updateModelDisplay();
    const activeTab = getActiveTab();
    if (activeTab) {
        updateSendButtonState(activeTab);
    }
}

function openSettingsWindow() {
    window.chrome.webview.postMessage('{ "method": "tools/call", "params": {"name": "openSettings", "arguments": {} } }');
}

settingsButton.addEventListener("click", openSettingsWindow);

let tabCounter = 1;
let tabs = {};
let activeTabId = "tab-chat-1";

tabs["tab-chat-1"] = {
    conversationHistory: [],
    isLoading: false
};

function getActiveTab() {
    return tabs[activeTabId];
}

function getActiveTabElements() {
    const tabNum = activeTabId.replace('tab-chat-', '');
    return {
        chatMessages: document.getElementById(`chatMessages-${tabNum}`),
        chatInput: document.getElementById(`chatInput-${tabNum}`),
        sendButton: document.getElementById(`sendButton-${tabNum}`)
    };
}

function addNewTab() {
    tabCounter++;
    const tabId = `tab-chat-${tabCounter}`;
    const tabNum = tabCounter;

    tabs[tabId] = {
        conversationHistory: [],
        isLoading: false
    };

    const tabList = document.querySelector('.chat-tabs menu[role="tablist"]');
    const tabLi = document.createElement("li");
    const tabButton = document.createElement("button");
    tabButton.setAttribute("role", "tab");
    tabButton.setAttribute("aria-selected", "false");
    tabButton.setAttribute("aria-controls", tabId);
    tabButton.setAttribute("id", `${tabId}-btn`);
    tabButton.innerHTML = `
        <span class="tab-title">チャット ${tabNum}</span>
        <span class="tab-close" aria-label="タブを閉じる" data-tab="${tabId}">×</span>
    `;
    tabButton.addEventListener("click", (e) => {
        if (!e.target.classList.contains('tab-close')) {
            switchTab(tabId);
        }
    });
    tabLi.appendChild(tabButton);
    tabList.appendChild(tabLi);

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

    switchTab(tabId);
    bindInputEvents(tabNum);
}

function switchTab(tabId) {
    document.querySelectorAll('.chat-tabs menu[role="tablist"] button[role="tab"]').forEach(t => {
        t.setAttribute("aria-selected", "false");
    });

    document.querySelectorAll('.chat-tabs article[role="tabpanel"]').forEach(panel => {
        panel.setAttribute("hidden", "true");
    });

    document.getElementById(`${tabId}-btn`).setAttribute("aria-selected", "true");
    document.getElementById(tabId).removeAttribute("hidden");

    activeTabId = tabId;

    const elems = getActiveTabElements();
    updateSendButtonState();
    elems.chatInput.focus();
}

function closeTab(tabId) {
    // 最後のタブは閉じられない
    const tabKeys = Object.keys(tabs);
    if (tabKeys.length <= 1) {
        return;
    }

    // 閉じるタブのインデックスを取得
    const tabIndex = tabKeys.indexOf(tabId);
    
    // 閉じるタブがアクティブな場合、隣のタブに切り替え
    if (activeTabId === tabId) {
        const newActiveIndex = tabIndex > 0 ? tabIndex - 1 : tabIndex + 1;
        const newActiveTabId = tabKeys[newActiveIndex];
        switchTab(newActiveTabId);
    }

    // タブデータを削除
    delete tabs[tabId];

    // タブボタンを削除
    const tabBtn = document.getElementById(`${tabId}-btn`);
    if (tabBtn) {
        tabBtn.parentElement.remove();
    }

    // タブパネルを削除
    const tabPanel = document.getElementById(tabId);
    if (tabPanel) {
        tabPanel.remove();
    }
}

// タブ閉じるボタンのイベントリスナー
document.querySelector('.chat-tabs menu[role="tablist"]').addEventListener('click', (e) => {
    if (e.target.classList.contains('tab-close')) {
        const tabId = e.target.getAttribute('data-tab');
        if (tabId) {
            closeTab(tabId);
        }
    }
});

function updateSendButtonState() {
    const elems = getActiveTabElements();
    const tab = getActiveTab();
    const message = elems.chatInput.value.trim();
    const hasApiKey = currentSettings.apiKey || currentSettings.endpointPreset === "ollama";
    elems.sendButton.disabled = !message || tab.isLoading || !hasApiKey;
}

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

const addTabButton = document.getElementById("addTabButton");
addTabButton.addEventListener("click", addNewTab);

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

    if (role === "assistant") {
        contentDiv.innerHTML = marked.parse(content);
    } else {
        contentDiv.textContent = content;
    }

    messageDiv.appendChild(contentDiv);

    elems.chatMessages.appendChild(messageDiv);
    elems.chatMessages.scrollTop = elems.chatMessages.scrollHeight;
}

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

    addMessage(message, "user");
    tab.conversationHistory.push({ role: "user", content: message });
    elems.chatInput.value = "";

    let assistantMessageDiv = null;
    let assistantContent = "";

    try {
        if (currentSettings.streaming) {
            assistantMessageDiv = createAssistantMessageDiv();

            const response = await fetch("/api/chat", {
                method: "POST",
                headers: { "Content-Type": "application/json" },
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

            tab.conversationHistory.push({ role: "assistant", content: assistantContent });
        } else {
            const response = await fetch("/api/chat", {
                method: "POST",
                headers: { "Content-Type": "application/json" },
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

function updateAssistantMessage(contentDiv, content) {
    contentDiv.innerHTML = marked.parse(content);
    const elems = getActiveTabElements();
    elems.chatMessages.scrollTop = elems.chatMessages.scrollHeight;
}

updateSendButtonState();
bindInputEvents(1);
updateModelDisplay();

// 最初のタブのクリックイベントを設定
const firstTabBtn = document.getElementById('tab-chat-1-btn');
if (firstTabBtn) {
    firstTabBtn.addEventListener("click", (e) => {
        if (!e.target.classList.contains('tab-close')) {
            switchTab('tab-chat-1');
        }
    });
    
    // 閉じる要素のイベント
    const firstCloseSpan = firstTabBtn.querySelector('.tab-close');
    if (firstCloseSpan) {
        firstCloseSpan.addEventListener('click', (e) => {
            e.stopPropagation();
            closeTab('tab-chat-1');
        });
    }
}

window.chrome.webview.addEventListener("message", (e) => {
    if (e.data === "settingsUpdated") {
        refreshSettings();
    }
});
