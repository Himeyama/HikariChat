// 設定画面用の JavaScript

const SETTINGS_KEY = "chatSettings";

// エンドポイント定義
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

// API 種別の説明
const apiTypeDescriptions = {
    chat_completions: "OpenAI 互換の API エンドポイントを使用します",
    responses: "OpenAI Responses API を使用します",
    anthropic: "Anthropic Claude API を使用します",
    gemini: "Google Gemini API を使用します"
};

// モデルリスト
const models = {
    openai: ["gpt-4o-mini", "gpt-4o", "gpt-4-turbo", "gpt-4", "gpt-3.5-turbo", "o1-preview", "o1-mini"],
    azure_openai: ["gpt-4o-mini", "gpt-4o", "gpt-4-turbo", "gpt-4", "gpt-3.5-turbo"],
    gemini: ["gemini-2.0-flash", "gemini-2.0-flash-lite", "gemini-1.5-pro", "gemini-1.5-flash"],
    grok: ["grok-4", "grok-4-fast", "grok-4-1-fast-reasoning", "grok-4-1-fast-non-reasoning", "grok-4-1-fast", "grok-code-fast-1", "grok-3", "grok-3-mini", "grok-3-beta", "grok-3-mini-beta"],
    anthropic: ["claude-sonnet-4-20250514", "claude-3-5-sonnet-latest", "claude-3-5-haiku-latest", "claude-3-opus-latest"],
    ollama: ["llama3.2", "llama3.1", "mistral", "gemma2", "qwen2.5"],
    custom: []
};

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

// 要素取得
const apiTypeSelect = document.getElementById("apiType");
const apiTypeInfo = document.getElementById("apiTypeInfo");
const endpointPresetSelect = document.getElementById("endpointPreset");
const apiEndpointInput = document.getElementById("apiEndpoint");
const apiKeyInput = document.getElementById("apiKey");
const modelSelect = document.getElementById("modelSelect");
const azureOpenAiGroup = document.getElementById("azureOpenAiGroup");
const azureDeploymentInput = document.getElementById("azureDeployment");
const saveSettings = document.getElementById("saveSettings");
const cancelSettings = document.getElementById("cancelSettings");
const closeButton = document.getElementById("closeButton");

// エンドポイント更新
function updateEndpoint() {
    const preset = endpointPresetSelect.value;
    const apiType = apiTypeSelect.value;
    
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
    
    // Azure OpenAI の表示切り替え
    azureOpenAiGroup.style.display = (preset === "azure_openai") ? "block" : "none";
    
    // モデルリスト更新
    updateModelList(preset);
}

// モデルリスト更新
function updateModelList(preset) {
    const currentModel = modelSelect.value;
    modelSelect.innerHTML = "";
    
    const modelList = models[preset] || models.custom;
    modelList.forEach(model => {
        const option = document.createElement("option");
        option.value = model;
        option.textContent = model;
        modelSelect.appendChild(option);
    });
    
    // 以前のモデルがリストにあれば選択
    if (modelList.includes(currentModel)) {
        modelSelect.value = currentModel;
    }
}

// API 種別変更時の処理
function updateApiType() {
    apiTypeInfo.textContent = apiTypeDescriptions[apiTypeSelect.value] || "";
    updateEndpoint();
}

// 初期値を設定
apiTypeSelect.value = currentSettings.apiType;
endpointPresetSelect.value = currentSettings.endpointPreset;
apiKeyInput.value = currentSettings.apiKey;
modelSelect.value = currentSettings.model;
azureDeploymentInput.value = currentSettings.azureDeployment || "";

// エンドポイント入力欄の設定
if (currentSettings.endpointPreset === "custom") {
    apiEndpointInput.disabled = false;
    apiEndpointInput.value = currentSettings.apiEndpoint;
} else {
    apiEndpointInput.disabled = true;
    const endpoint = endpoints[currentSettings.endpointPreset]?.[currentSettings.apiType] || currentSettings.apiEndpoint;
    apiEndpointInput.value = endpoint;
}

updateModelList(currentSettings.endpointPreset);
updateApiType();

// イベントリスナー
apiTypeSelect.addEventListener("change", updateApiType);
endpointPresetSelect.addEventListener("change", updateEndpoint);

// 保存ボタン
saveSettings.addEventListener("click", () => {
    const preset = endpointPresetSelect.value;
    const apiType = apiTypeSelect.value;
    let apiEndpoint = apiEndpointInput.value.trim();
    
    // エンドポイントが空の場合、プリセットから取得
    if (!apiEndpoint) {
        apiEndpoint = endpoints[preset]?.[apiType] || "";
    }
    
    // それでも空の場合はエラー
    if (!apiEndpoint && preset !== "custom") {
        alert("この API 種別とエンドポイントの組み合わせはサポートされていません。");
        return;
    }
    
    currentSettings = {
        apiType: apiType,
        endpointPreset: preset,
        apiEndpoint: apiEndpoint,
        apiKey: apiKeyInput.value.trim(),
        model: modelSelect.value,
        azureDeployment: azureDeploymentInput.value.trim()
    };
    saveSettingsToStorage(currentSettings);

    // メインウィンドウに設定更新を通知
    window.chrome.webview.postMessage('{ "method": "tools/call", "params": {"name": "settingsUpdated", "arguments": {} } }');

    // C# 側に設定画面を閉じるメッセージを送信
    window.chrome.webview.postMessage('{ "method": "tools/call", "params": {"name": "closeSettings", "arguments": {} } }');
});

// キャンセルボタン
cancelSettings.addEventListener("click", () => {
    window.chrome.webview.postMessage('{ "method": "tools/call", "params": {"name": "closeSettings", "arguments": {} } }');
});

// 閉じるボタン
closeButton.addEventListener("click", () => {
    window.chrome.webview.postMessage('{ "method": "tools/call", "params": {"name": "closeSettings", "arguments": {} } }');
});
