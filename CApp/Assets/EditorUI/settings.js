// 設定画面用の JavaScript

const SETTINGS_KEY = "chatSettings";

// Ollama の状態
let ollamaAvailable = false;
let ollamaModels = [];

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

// 各 API 種別に対応するエンドポイントプリセット
const compatibleEndpoints = {
    chat_completions: ["openai", "azure_openai", "grok", "ollama", "custom"],
    responses: ["openai", "custom"],
    anthropic: ["anthropic", "custom"],
    gemini: ["gemini", "custom"]
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
    openai: [
        "gpt-5.2",          // 最新フラッグシップ推論モデル
        "gpt-5.2-pro",      // 高精度版
        "gpt-5",            // 前世代フラッグシップ
        "gpt-5-mini",       // コスト効率重視版
        "gpt-5-nano",       // 最速・最安
        "gpt-4.1",          // コーディング特化
        "gpt-4.1-mini",     // gpt-4.1 軽量版
        "gpt-4.1-nano",     // 最速軽量版
        "gpt-4o",           // マルチモーダル
        "gpt-4o-mini",      // gpt-4o 軽量版
    ],
    azure_openai: [
        "gpt-5",
        "gpt-5-mini",
        "gpt-4.1",
        "gpt-4.1-mini",
        "gpt-4o",
        "gpt-4o-mini",
    ],
    gemini: [
        "gemini-3-pro",          // 最新フラッグシップ (preview)
        "gemini-3-flash",        // 高性能マルチモーダル (preview)
        "gemini-2.5-pro",        // GA 推論モデル
        "gemini-2.5-flash",      // GA 高速・低コスト
        "gemini-2.5-flash-lite", // GA 最軽量
        "gemini-2.0-flash",      // 前世代 (2026/3 退役予定)
        "gemini-2.0-flash-lite", // 前世代軽量版 (2026/3 退役予定)
    ],
    grok: [
        "grok-4",
        "grok-4-fast",
        "grok-4-1-fast-reasoning",
        "grok-4-1-fast-non-reasoning",
        "grok-4-1-fast",
        "grok-code-fast-1",
        "grok-3",
        "grok-3-mini",
        "grok-3-beta",
        "grok-3-mini-beta",
    ],
    anthropic: [
        "claude-opus-4-6",    // 最新・最高性能 (2026/2 リリース)
        "claude-sonnet-4-6",  // 最新ミッドティア (2026/2 リリース)
        "claude-opus-4-5",    // 前世代フラッグシップ
        "claude-sonnet-4-5",  // 前世代ミッドティア
        "claude-haiku-4-5",   // 軽量版
    ],
    ollama: [
        "llama3.3",         // Meta 最新
        "llama3.2",         // Meta 軽量
        "qwen3",            // Alibaba 最新世代
        "qwen2.5",          // Alibaba 安定版
        "deepseek-r1",      // 推論特化
        "mistral",          // Mistral 7B
        "gemma2",           // Google 軽量版
        "phi4",             // Microsoft 14B
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
    mcp: {
        enabled: false,
        mcpServers: {}
    }
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
const streamingCheckbox = document.getElementById("streaming");
const saveSettings = document.getElementById("saveSettings");
const cancelSettings = document.getElementById("cancelSettings");
const closeButton = document.getElementById("closeButton");

// MCP 設定用要素
const mcpEnabledCheckbox = document.getElementById("mcpEnabled");
const mcpJsonTextarea = document.getElementById("mcpJson");

// エンドポイント更新
function updateEndpoint() {
    const preset = endpointPresetSelect.value;
    const apiType = apiTypeSelect.value;

    // 選択可能なエンドポイントを更新
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

    // Azure OpenAI の表示切り替え
    azureOpenAiGroup.style.display = (preset === "azure_openai") ? "block" : "none";

    // モデルリスト更新
    updateModelList(preset);
}

// API 種別に対応するエンドポイントオプションを更新
function updateEndpointOptions(apiType) {
    const currentPreset = endpointPresetSelect.value;
    const compatible = compatibleEndpoints[apiType] || [];

    console.log("updateEndpointOptions called:", { apiType, currentPreset, compatible, ollamaAvailable });

    // 現在のプリセットが非対応の場合、custom に変更
    if (!compatible.includes(currentPreset)) {
        endpointPresetSelect.value = "custom";
    }

    // オプションの表示/非表示を切り替え
    Array.from(endpointPresetSelect.options).forEach(option => {
        const value = option.value;
        
        // Ollama は利用可能な場合のみ表示
        if (value === "ollama" && !ollamaAvailable) {
            option.style.display = "none";
            option.disabled = true;
            console.log("Ollama option hidden (not available)");
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
    
    console.log("updateEndpointOptions completed");
}

// モデルリスト更新
function updateModelList(preset) {
    const currentModel = modelSelect.value;
    modelSelect.innerHTML = "";

    let modelList;
    
    // Ollama の場合は動的に取得したモデル一覧を使用
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
apiKeyInput.value = currentSettings.apiKey;
modelSelect.value = currentSettings.model;
azureDeploymentInput.value = currentSettings.azureDeployment || "";
streamingCheckbox.checked = currentSettings.streaming;
mcpEnabledCheckbox.checked = currentSettings.mcp?.enabled || false;

// MCP 設定を JSON 形式で表示
if (currentSettings.mcp?.mcpServers && Object.keys(currentSettings.mcp.mcpServers).length > 0) {
    mcpJsonTextarea.value = JSON.stringify({ mcpServers: currentSettings.mcp.mcpServers }, null, 2);
} else {
    mcpJsonTextarea.value = "";
}

// エンドポイントプリセットを API 種別に基づいて設定
const compatible = compatibleEndpoints[currentSettings.apiType] || [];
if (compatible.includes(currentSettings.endpointPreset)) {
    // Ollama の場合は、後で C# から ollamaInfo が来るまで保留
    if (currentSettings.endpointPreset === "ollama") {
        endpointPresetSelect.value = "custom"; // 一時的に custom に設定
    } else {
        endpointPresetSelect.value = currentSettings.endpointPreset;
    }
} else {
    endpointPresetSelect.value = "custom";
}

// エンドポイント入力欄の設定
if (currentSettings.endpointPreset === "custom") {
    apiEndpointInput.disabled = false;
    apiEndpointInput.value = currentSettings.apiEndpoint;
} else {
    apiEndpointInput.disabled = true;
    const endpoint = endpoints[currentSettings.endpointPreset]?.[currentSettings.apiType] || currentSettings.apiEndpoint;
    apiEndpointInput.value = endpoint;
}

updateEndpointOptions(currentSettings.apiType);
updateModelList(currentSettings.endpointPreset);
updateApiType();

// C# 側に Ollama 情報を要求
window.chrome.webview.postMessage('{ "method": "tools/call", "params": {"name": "getOllamaInfo", "arguments": {} } }');

// イベントリスナー
apiTypeSelect.addEventListener("change", updateApiType);
endpointPresetSelect.addEventListener("change", updateEndpoint);

// C# からのメッセージを処理
window.chrome.webview.addEventListener("message", (e) => {
    try {
        const data = JSON.parse(e.data);
        console.log("Received message:", data);
        if (data.method === "ollamaInfo") {
            ollamaAvailable = data.isAvailable || false;
            ollamaModels = data.models || [];
            console.log("Ollama info updated:", ollamaAvailable, ollamaModels);
            
            // 以前 Ollama が選択されていた場合は復元
            if (currentSettings.endpointPreset === "ollama" && ollamaAvailable && ollamaModels.length > 0) {
                endpointPresetSelect.value = "ollama";
            }
            
            // UI を更新
            updateEndpointOptions(apiTypeSelect.value);
            
            // 現在 ollama が選択されている場合はモデルリストも更新
            if (endpointPresetSelect.value === "ollama") {
                updateModelList("ollama");
            }
        }
    } catch (err) {
        console.error("Message processing error:", err);
    }
});

// デバッグ用：Ollama 利用可能フラグを手動設定
function setOllamaAvailable(available) {
    window.chrome.webview.postMessage(JSON.stringify({
        method: "tools/call",
        params: {
            name: "setOllamaAvailable",
            arguments: { available: available.toString() }
        }
    }));
}

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

    // MCP 設定を JSON から解析
    let mcpServers = {};
    const mcpJson = mcpJsonTextarea.value.trim();
    if (mcpJson) {
        try {
            const parsed = JSON.parse(mcpJson);
            mcpServers = parsed.mcpServers || {};
        } catch (e) {
            alert("MCP 設定の JSON 形式が不正です：" + e.message);
            return;
        }
    }

    currentSettings = {
        apiType: apiType,
        endpointPreset: preset,
        apiEndpoint: apiEndpoint,
        apiKey: apiKeyInput.value.trim(),
        model: modelSelect.value,
        azureDeployment: azureDeploymentInput.value.trim(),
        streaming: streamingCheckbox.checked,
        mcp: {
            enabled: mcpEnabledCheckbox.checked,
            mcpServers: mcpServers
        }
    };
    saveSettingsToStorage(currentSettings);

    // C# 側に MCP 設定を保存
    // 最新の currentSettings を使用
    window.chrome.webview.postMessage(JSON.stringify({
        method: "tools/call",
        params: {
            name: "saveMcpSettings",
            arguments: {
                mcpJson: JSON.stringify(currentSettings.mcp)
            }
        }
    }));

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

// タブ切り替え機能
const tabButtons = document.querySelectorAll('[role="tab"]');
const tabPanels = document.querySelectorAll('[role="tabpanel"]');

tabButtons.forEach(tab => {
    tab.addEventListener("click", () => {
        // すべてのタブの選択状態を解除
        tabButtons.forEach(t => t.setAttribute("aria-selected", "false"));
        // クリックされたタブを選択状態に
        tab.setAttribute("aria-selected", "true");

        // すべてのパネルを非表示
        tabPanels.forEach(panel => panel.setAttribute("hidden", "true"));
        // 対応するパネルを表示
        const targetPanel = document.getElementById(tab.getAttribute("aria-controls"));
        if (targetPanel) {
            targetPanel.removeAttribute("hidden");
        }
    });
});
