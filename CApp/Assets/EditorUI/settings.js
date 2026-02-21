// 設定画面用の JavaScript

const SETTINGS_KEY = "chatSettings";

let ollamaAvailable = false;
let ollamaModels = [];

const endpoints = {
    openai: {
        chat_completions: "https://api.openai.com/v1/chat/completions",
        responses: "https://api.openai.com/v1/responses",
        anthropic: "",
        gemini: ""
    },
    azure_openai: {
        chat_completions: "https://{resource}.openai.azure.com/openai/deployments/{deployment}/chat/completions?api-version=2024-08-01-preview",
        responses: "",
        anthropic: "",
        gemini: ""
    },
    gemini: {
        chat_completions: "",
        responses: "",
        anthropic: "",
        gemini: "https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent"
    },
    grok: {
        chat_completions: "https://api.x.ai/v1/chat/completions",
        responses: "",
        anthropic: "",
        gemini: ""
    },
    anthropic: {
        chat_completions: "",
        responses: "",
        anthropic: "https://api.anthropic.com/v1/messages",
        gemini: ""
    },
    ollama: {
        chat_completions: "http://localhost:11434/v1/chat/completions",
        responses: "",
        anthropic: "",
        gemini: ""
    },
    custom: {
        chat_completions: "",
        responses: "",
        anthropic: "",
        gemini: ""
    }
};

const compatibleEndpoints = {
    chat_completions: ["openai", "azure_openai", "grok", "ollama", "custom"],
    responses: ["openai", "custom"],
    anthropic: ["anthropic", "custom"],
    gemini: ["gemini", "custom"]
};

const apiTypeDescriptions = {
    chat_completions: "OpenAI 互換の API エンドポイントを使用します",
    responses: "OpenAI Responses API を使用します",
    anthropic: "Anthropic Claude API を使用します",
    gemini: "Google Gemini API を使用します"
};

const models = {
    openai: [
        "gpt-5.2", "gpt-5.2-pro", "gpt-5", "gpt-5-mini", "gpt-5-nano",
        "gpt-4.1", "gpt-4.1-mini", "gpt-4.1-nano", "gpt-4o", "gpt-4o-mini",
    ],
    azure_openai: [
        "gpt-5", "gpt-5-mini", "gpt-4.1", "gpt-4.1-mini", "gpt-4o", "gpt-4o-mini",
    ],
    gemini: [
        "gemini-3-pro", "gemini-3-flash", "gemini-2.5-pro", "gemini-2.5-flash",
        "gemini-2.5-flash-lite", "gemini-2.0-flash", "gemini-2.0-flash-lite",
    ],
    grok: [
        "grok-4", "grok-4-fast", "grok-4-1-fast-reasoning", "grok-4-1-fast-non-reasoning",
        "grok-4-1-fast", "grok-code-fast-1", "grok-3", "grok-3-mini",
        "grok-3-beta", "grok-3-mini-beta",
    ],
    anthropic: [
        "claude-opus-4-6", "claude-sonnet-4-6", "claude-opus-4-5",
        "claude-sonnet-4-5", "claude-haiku-4-5",
    ],
    ollama: [
        "llama3.3", "llama3.2", "qwen3", "qwen2.5", "deepseek-r1",
        "mistral", "gemma2", "phi4",
    ],
    custom: []
};

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

const apiTypeSelect = document.getElementById("apiType");
const apiTypeInfo = document.getElementById("apiTypeInfo");
const endpointPresetSelect = document.getElementById("endpointPreset");
const apiEndpointInput = document.getElementById("apiEndpoint");
const apiKeyInput = document.getElementById("apiKey");
const modelSelect = document.getElementById("modelSelect");
const azureOpenAiGroup = document.getElementById("azureOpenAiGroup");
const azureDeploymentInput = document.getElementById("azureDeployment");
const streamingCheckbox = document.getElementById("streaming");
const mcpEnabledCheckbox = document.getElementById("mcpEnabled");
const mcpServersJsonTextarea = document.getElementById("mcpServersJson");
const saveSettings = document.getElementById("saveSettings");
const cancelSettings = document.getElementById("cancelSettings");
const closeButton = document.getElementById("closeButton");

function updateEndpoint() {
// ... (中略: updateEndpoint 以降の既存コードを維持しつつ、初期値をセット)
    const preset = endpointPresetSelect.value;
    const apiType = apiTypeSelect.value;

    updateEndpointOptions(apiType);

    if (preset === "custom") {
        apiEndpointInput.disabled = false;
        apiEndpointInput.placeholder = "https://example.com/api";
    } else {
        apiEndpointInput.disabled = true;
        const endpoint = endpoints[preset]?.[apiType] || "";
        if (endpoint) {
            apiEndpointInput.value = endpoint;
            apiEndpointInput.placeholder = endpoint;
        } else {
            apiEndpointInput.value = "";
            apiEndpointInput.placeholder = "この API 種別はサポートされていません";
        }
    }

    azureOpenAiGroup.style.display = (preset === "azure_openai") ? "block" : "none";
    updateModelList(preset);
}

function updateEndpointOptions(apiType) {
    const currentPreset = endpointPresetSelect.value;
    const compatible = compatibleEndpoints[apiType] || [];

    if (!compatible.includes(currentPreset)) {
        endpointPresetSelect.value = "custom";
    }

    Array.from(endpointPresetSelect.options).forEach(option => {
        const value = option.value;

        if (value === "ollama" && !ollamaAvailable) {
            option.style.display = "none";
            option.disabled = true;
            return;
        }

        if (compatible.includes(value)) {
            option.style.display = "";
            option.disabled = false;
        } else {
            option.style.display = "none";
            option.disabled = true;
        }
    });
}

function updateModelList(preset, targetModel = null) {
    const currentModel = targetModel || modelSelect.value;
    modelSelect.innerHTML = "";

    let modelList;

    if (preset === "ollama" && ollamaModels.length > 0) {
        modelList = ollamaModels;
    } else {
        modelList = models[preset] || models.custom;
    }

    modelList.forEach(model => {
        const option = document.createElement("option");
        option.value = model;
        option.textContent = model;
        modelSelect.appendChild(option);
    });

    if (currentModel && modelList.includes(currentModel)) {
        modelSelect.value = currentModel;
    }
}

function updateApiType() {
    apiTypeInfo.textContent = apiTypeDescriptions[apiTypeSelect.value] || "";
    updateEndpoint();
}

apiTypeSelect.value = currentSettings.apiType;
apiKeyInput.value = currentSettings.apiKey;
azureDeploymentInput.value = currentSettings.azureDeployment || "";
streamingCheckbox.checked = currentSettings.streaming;
mcpEnabledCheckbox.checked = currentSettings.mcpEnabled || false;
mcpServersJsonTextarea.value = JSON.stringify(currentSettings.mcpServers || {}, null, 2);

const compatible = compatibleEndpoints[currentSettings.apiType] || [];
if (compatible.includes(currentSettings.endpointPreset)) {
    if (currentSettings.endpointPreset === "ollama") {
        endpointPresetSelect.value = "custom";
    } else {
        endpointPresetSelect.value = currentSettings.endpointPreset;
    }
} else {
    endpointPresetSelect.value = "custom";
}

if (currentSettings.endpointPreset === "custom") {
    apiEndpointInput.disabled = false;
    apiEndpointInput.value = currentSettings.apiEndpoint;
} else {
    apiEndpointInput.disabled = true;
    const endpoint = endpoints[currentSettings.endpointPreset]?.[currentSettings.apiType] || currentSettings.apiEndpoint;
    apiEndpointInput.value = endpoint;
}

updateEndpointOptions(currentSettings.apiType);
updateModelList(currentSettings.endpointPreset, currentSettings.model);
updateApiType();

window.chrome.webview.postMessage('{ "method": "tools/call", "params": {"name": "getOllamaInfo", "arguments": {} } }');

apiTypeSelect.addEventListener("change", updateApiType);
endpointPresetSelect.addEventListener("change", updateEndpoint);

window.chrome.webview.addEventListener("message", (e) => {
    try {
        const data = JSON.parse(e.data);
        console.log("Received message:", data);
        if (data.method === "ollamaInfo") {
            ollamaAvailable = data.isAvailable || false;
            ollamaModels = data.models || [];
            console.log("Ollama info updated:", ollamaAvailable, ollamaModels);

            if (currentSettings.endpointPreset === "ollama" && ollamaAvailable && ollamaModels.length > 0) {
                endpointPresetSelect.value = "ollama";
            }

            updateEndpointOptions(apiTypeSelect.value);

            if (endpointPresetSelect.value === "ollama") {
                updateModelList("ollama");
            }
        }
    } catch (err) {
        console.error("Message processing error:", err);
    }
});

function setOllamaAvailable(available) {
    window.chrome.webview.postMessage(JSON.stringify({
        method: "tools/call",
        params: {
            name: "setOllamaAvailable",
            arguments: { available: available.toString() }
        }
    }));
}

saveSettings.addEventListener("click", () => {
    const preset = endpointPresetSelect.value;
    const apiType = apiTypeSelect.value;
    let apiEndpoint = apiEndpointInput.value.trim();

    if (!apiEndpoint) {
        apiEndpoint = endpoints[preset]?.[apiType] || "";
    }

    if (!apiEndpoint && preset !== "custom") {
        alert("この API 種別とエンドポイントの組み合わせはサポートされていません。");
        return;
    }

    let mcpServers = {};
    try {
        mcpServers = JSON.parse(mcpServersJsonTextarea.value || "{}");
    } catch (e) {
        alert("MCP サーバー設定の JSON 形式が正しくありません。");
        return;
    }

    currentSettings = {
        apiType: apiType,
        endpointPreset: preset,
        apiEndpoint: apiEndpoint,
        apiKey: apiKeyInput.value.trim(),
        model: modelSelect.value,
        azureDeployment: azureDeploymentInput.value.trim(),
        streaming: streamingCheckbox.checked,
        mcpEnabled: mcpEnabledCheckbox.checked,
        mcpServers: mcpServers
    };
    saveSettingsToStorage(currentSettings);

    // C# 側に設定を保存
    window.chrome.webview.postMessage(JSON.stringify({
        method: "tools/call",
        params: {
            name: "saveSettings",
            arguments: {
                settingsJson: JSON.stringify(currentSettings)
            }
        }
    }));

    // メインウィンドウに設定更新を通知
    window.chrome.webview.postMessage('{ "method": "tools/call", "params": {"name": "settingsUpdated", "arguments": {} } }');

    // 設定画面を閉じる
    window.chrome.webview.postMessage('{ "method": "tools/call", "params": {"name": "closeSettings", "arguments": {} } }');
});

cancelSettings.addEventListener("click", () => {
    window.chrome.webview.postMessage('{ "method": "tools/call", "params": {"name": "closeSettings", "arguments": {} } }');
});

closeButton.addEventListener("mousedown", () => {
    window.chrome.webview.postMessage('{ "method": "tools/call", "params": {"name": "closeSettings", "arguments": {} } }');
});

const tabButtons = document.querySelectorAll('[role="tab"]');
const tabPanels = document.querySelectorAll('[role="tabpanel"]');

tabButtons.forEach(tab => {
    tab.addEventListener("click", () => {
        tabButtons.forEach(t => t.setAttribute("aria-selected", "false"));
        tab.setAttribute("aria-selected", "true");

        tabPanels.forEach(panel => panel.setAttribute("hidden", "true"));
        const targetPanel = document.getElementById(tab.getAttribute("aria-controls"));
        if (targetPanel) {
            targetPanel.removeAttribute("hidden");
        }
    });
});
