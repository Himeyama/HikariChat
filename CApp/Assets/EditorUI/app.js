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

btnMin.addEventListener("mousedown", () => minimizeCommand());
btnMax.addEventListener("mousedown", () => {
    toggleMaximize();
});
btnRestore.addEventListener("mousedown", () => {
    toggleMaximize();
});
btnClose.addEventListener("mousedown", () => closeCommand());

const settingsButton = document.getElementById("settingsButton");
const SETTINGS_KEY = "chatSettings";

const defaultSettings = {
    apiType: "chat_completions",
    endpointPreset: "openai",
    apiEndpoint: "https://api.openai.com/v1/chat/completions",
    apiKey: "",
    model: "gpt-4o-mini",
    azureDeployment: "",
    streaming: true,
    mcpEnabled: false,
    mcpServers: {}
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

function updateMcpStatusDisplay(status) {
    const display = document.getElementById('mcpStatusDisplay');
    if (!display) return;
    
    // 引数がない場合は単に現在の設定に基づいた表示
    if (!status) {
        if (!currentSettings.mcpEnabled) {
            display.textContent = "MCP: 無効";
            display.className = "mcp-status-display disabled";
        } else {
            display.textContent = "MCP: 準備中...";
            display.className = "mcp-status-display not-ready";
        }
        return;
    }

    if (!status.enabled) {
        display.textContent = "MCP: 無効";
        display.className = "mcp-status-display disabled";
    } else if (status.totalCount > 0 && status.activeCount === status.totalCount) {
        display.textContent = `MCP: 準備完了 (${status.activeCount} サーバー)`;
        display.className = "mcp-status-display ready";
    } else if (status.totalCount === 0) {
        display.textContent = "MCP: サーバー未登録";
        display.className = "mcp-status-display not-ready";
    } else {
        display.textContent = `MCP: 未準備 (${status.activeCount}/${status.totalCount})`;
        display.className = "mcp-status-display not-ready";
    }
}

function refreshSettings() {
    currentSettings = loadSettings();
    console.log('[Settings Updated]', currentSettings);
    updateModelDisplay();
    updateMcpStatusDisplay();
    requestMcpStatus();
    const activeTab = getActiveTab();
    if (activeTab) {
        updateSendButtonState(activeTab);
    }
}

function requestMcpStatus() {
    window.chrome.webview.postMessage('{ "method": "tools/call", "params": {"name": "getMcpInfo", "arguments": {} } }');
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

async function executeMcpTool(name, args) {
    try {
        const response = await fetch("/api/mcp/execute", {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify({ name, arguments: args })
        });
        if (!response.ok) {
            const error = await response.json();
            return { error: error.error || "Execution failed" };
        }
        return await response.json();
    } catch (e) {
        return { error: e.message };
    }
}

async function sendMessage() {
    const elems = getActiveTabElements();
    const tab = getActiveTab();
    const message = elems.chatInput.value.trim();
    if (!message || tab.isLoading) return;

    if (!currentSettings.apiKey && currentSettings.endpointPreset !== "ollama") {
        addMessage("API キーが設定されていません。設定から入力してください。", "error");
        openSettingsWindow();
        return;
    }

    tab.isLoading = true;
    updateSendButtonState();

    if (message) {
        addMessage(message, "user");
        tab.conversationHistory.push({ role: "user", content: message });
        elems.chatInput.value = "";
    }

    try {
        let continueLoop = true;
        while (continueLoop) {
            continueLoop = false;
            let assistantMessageDiv = null;
            let assistantContent = "";
            let toolCalls = [];

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
                        streaming: true,
                        mcpEnabled: currentSettings.mcpEnabled
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
                                const delta = parsed.choices?.[0]?.delta;

                                if (delta) {
                                    if (delta.content) {
                                        assistantContent += delta.content;
                                        updateAssistantMessage(assistantMessageDiv, assistantContent);
                                    }
                                    
                                    if (delta.tool_calls) {
                                        for (const tc of delta.tool_calls) {
                                            if (!toolCalls[tc.index]) {
                                                toolCalls[tc.index] = { id: tc.id, name: "", arguments: "" };
                                            }
                                            if (tc.function?.name) toolCalls[tc.index].name += tc.function.name;
                                            if (tc.function?.arguments) toolCalls[tc.index].arguments += tc.function.arguments;
                                        }
                                    }
                                }
                            } catch (e) { }
                        }
                    }
                }
            } else {
                // 非ストリーミング
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
                        streaming: false,
                        mcpEnabled: currentSettings.mcpEnabled
                    })
                });

                if (!response.ok) {
                    const errorData = await response.json();
                    throw new Error(errorData.error || "API エラーが発生しました");
                }

                const data = await response.json();
                const message = data.choices?.[0]?.message;
                if (message) {
                    assistantContent = message.content || "";
                    if (message.tool_calls) {
                        toolCalls = message.tool_calls.map(tc => ({
                            id: tc.id,
                            name: tc.function.name,
                            arguments: tc.function.arguments
                        }));
                    }
                    if (assistantContent) {
                        addMessage(assistantContent, "assistant");
                    }
                }
            }

            // メッセージ履歴に追加
            const assistantMsg = { role: "assistant", content: assistantContent };
            if (toolCalls.length > 0) {
                assistantMsg.tool_calls = toolCalls.map(tc => ({
                    id: tc.id,
                    type: "function",
                    function: { name: tc.name, arguments: tc.arguments }
                }));
            }
            tab.conversationHistory.push(assistantMsg);

            // ツール実行
            if (toolCalls.length > 0) {
                if (!assistantMessageDiv) {
                    assistantMessageDiv = createAssistantMessageDiv();
                }
                
                for (const tc of toolCalls.filter(x => x)) {
                    updateAssistantMessage(assistantMessageDiv, `${assistantContent}\n\n> ツール実行中: \`${tc.name}\`...`);
                    
                    let args = {};
                    try { args = JSON.parse(tc.arguments); } catch (e) { }
                    
                    const result = await executeMcpTool(tc.name, args);
                    const resultString = JSON.stringify(result);
                    
                    tab.conversationHistory.push({
                        role: "tool",
                        tool_call_id: tc.id,
                        name: tc.name,
                        content: resultString
                    });
                    
                    assistantContent += `\n\n> ツール \`${tc.name}\` の実行が完了しました。`;
                    updateAssistantMessage(assistantMessageDiv, assistantContent);
                }
                continueLoop = true; // 再度 AI を呼び出す
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
updateMcpStatusDisplay();

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
    } else {
        try {
            const data = JSON.parse(e.data);
            if (data.method === "mcpStatus") {
                updateMcpStatusDisplay(data);
            }
        } catch(e) {}
    }
});

// 初期ロード時にステータスを要求
requestMcpStatus();
