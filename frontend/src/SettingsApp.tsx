import { useState, useEffect } from 'react';
import { Box, Button, Flex, Tabs, TextField, Select, Switch, TextArea, Text } from '@radix-ui/themes';
import './App.css'; // Reuse general styles like .window, .title-bar etc.

const presetDisplayNames: Record<string, string> = {
    openai: "OpenAI",
    azure_openai: "Azure OpenAI",
    custom: "カスタム",
};

// Mock WebView2 communication for now
const mockWebView2 = {
    postMessage: (message: string) => console.log('WebView2 Post Message (Settings):', message),
    addEventListener: (_event: string, _callback: (e: any) => void) => { },
    removeEventListener: (_event: string, _callback: (e: any) => void) => { },
};
const webview = (window as any).chrome?.webview || mockWebView2;

interface Settings {
    apiType: string;
    endpointPreset: string;
    apiEndpoint: string;
    apiKey: string;
    model: string;
    azureDeployment: string;
    streaming: boolean;
    mcpEnabled: boolean;
    mcpServers: Record<string, any>;
}

const defaultSettings: Settings = {
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

function loadSettings(): Settings {
    const saved = localStorage.getItem("chatSettings");
    if (saved) {
        try {
            return { ...defaultSettings, ...JSON.parse(saved) };
        } catch {
            return defaultSettings;
        }
    }
    return defaultSettings;
}

const endpoints = {
    openai: {
        chat_completions: "https://api.openai.com/v1/chat/completions",
        azure: "",
        claude: "",
        gemini: ""
    },
    azure_openai: {
        chat_completions: "https://{resource}.openai.azure.com/openai/deployments/{deployment}/chat/completions?api-version=2024-08-01-preview",
        azure: "https://{resource}.openai.azure.com/openai/deployments/{deployment}/chat/completions?api-version=2024-08-01-preview",
        claude: "",
        gemini: ""
    },
    gemini: {
        chat_completions: "",
        azure: "",
        claude: "",
        gemini: "https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent"
    },
    grok: {
        chat_completions: "https://api.x.ai/v1/chat/completions",
        azure: "",
        claude: "",
        gemini: ""
    },
    anthropic: {
        chat_completions: "",
        azure: "",
        claude: "https://api.anthropic.com/v1/messages",
        gemini: ""
    },
    ollama: {
        chat_completions: "http://localhost:11434/v1/chat/completions",
        azure: "",
        claude: "",
        gemini: ""
    },
    custom: {
        chat_completions: "",
        azure: "",
        claude: "",
        gemini: ""
    }
};

type ApiType = 'azure' | 'gemini' | 'claude' | 'chat_completions';
type EndpointPreset = 'openai' | 'azure_openai' | 'gemini' | 'grok' | 'anthropic' | 'ollama' | 'custom';

const compatibleEndpoints = {
    chat_completions: ["openai", "azure_openai", "grok", "ollama", "custom"],
    azure: ["azure_openai", "custom"],
    claude: ["anthropic", "custom"],
    gemini: ["gemini", "custom"]
};

// apiType 変更時のデフォルトプリセットマッピング
const defaultPresetForApiType: Record<ApiType, EndpointPreset> = {
    chat_completions: "openai",
    azure: "azure_openai",
    claude: "anthropic",
    gemini: "gemini",
};

const apiTypeDescriptions: Record<ApiType, string> = {
    chat_completions: "OpenAI 互換の API エンドポイントを使用します（OpenAI、Grok、Ollama など）",
    azure: "Azure OpenAI Service を使用します",
    claude: "Anthropic Claude API を使用します",
    gemini: "Google Gemini API を使用します"
};

// "custom" はモデル手入力用のセンチネル値
const CUSTOM_MODEL_VALUE = "__custom__";

const models: Record<EndpointPreset | string, string[]> = {
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

function SettingsApp() {
    const initialSettings = loadSettings();
    const [ollamaAvailable, setOllamaAvailable] = useState(false);
    const [ollamaModels, setOllamaModels] = useState<string[]>([]);
    const [activeTab, setActiveTab] = useState('tab-api');
    const [saveError, setSaveError] = useState<string | null>(null);

    const [apiType, setApiType] = useState<ApiType>(initialSettings.apiType as ApiType);
    const [endpointPreset, setEndpointPreset] = useState<EndpointPreset>(initialSettings.endpointPreset as EndpointPreset);
    const [apiEndpoint, setApiEndpoint] = useState(initialSettings.apiEndpoint);
    const [apiKey, setApiKey] = useState(initialSettings.apiKey);
    const [model, setModel] = useState(initialSettings.model);
    // カスタムモデル入力用: プリセットがcustomのとき、またはモデルがリストにないときに使う
    const [customModelInput, setCustomModelInput] = useState(() => {
        // 初期値: モデルが既知リストにない場合はその値をカスタム入力欄に引き継ぐ
        const preset = initialSettings.endpointPreset as EndpointPreset;
        const list = models[preset] || [];
        return !list.includes(initialSettings.model) ? initialSettings.model : "";
    });
    const [isCustomModel, setIsCustomModel] = useState(() => {
        const preset = initialSettings.endpointPreset as EndpointPreset;
        if (preset === "custom") return true;
        const list = models[preset] || [];
        return !list.includes(initialSettings.model);
    });
    const [azureDeployment, setAzureDeployment] = useState(initialSettings.azureDeployment);
    const [streaming, setStreaming] = useState(initialSettings.streaming);
    const [mcpEnabled, setMcpEnabled] = useState(initialSettings.mcpEnabled);
    const [mcpServersJson, setMcpServersJson] = useState(JSON.stringify(initialSettings.mcpServers || {}, null, 2));

    // エンドポイントプリセット変更時の副作用
    useEffect(() => {
        if (endpointPreset !== "custom") {
            // プリセットが変わったらエンドポイントURLを自動設定
            const endpoint = endpoints[endpointPreset]?.[apiType] || "";
            setApiEndpoint(endpoint);
        }

        // カスタムプリセットに切り替えた場合は常にモデル手入力モードへ
        if (endpointPreset === "custom") {
            setIsCustomModel(true);
            // 既存のモデル値をカスタム入力欄に引き継ぐ
            setCustomModelInput(prev => prev || model);
        } else {
            // プリセット変更時にモデルリストを評価
            const list = getModelList(endpointPreset);
            if (list.includes(model)) {
                setIsCustomModel(false);
            } else {
                // リストにない場合は最初のモデルを選択
                setIsCustomModel(false);
                setModel(list[0] || "");
            }
        }
    }, [endpointPreset]);

    // apiType 変更時: 互換するデフォルトプリセットへ切り替え
    useEffect(() => {
        const compatible = compatibleEndpoints[apiType] || [];
        if (!compatible.includes(endpointPreset)) {
            const newPreset = defaultPresetForApiType[apiType] || "custom";
            setEndpointPreset(newPreset);
            // エンドポイントURLも更新
            if (newPreset !== "custom") {
                const endpoint = endpoints[newPreset]?.[apiType] || "";
                setApiEndpoint(endpoint);
            }
        } else if (endpointPreset !== "custom") {
            // 同じプリセットのままapiTypeが変わった場合もURLを更新
            const endpoint = endpoints[endpointPreset]?.[apiType] || "";
            setApiEndpoint(endpoint);
        }
    }, [apiType]);

    // Ollama情報取得
    useEffect(() => {
        requestOllamaInfo();

        const handleWebviewMessage = (event: any) => {
            try {
                const data = JSON.parse(event.data);
                if (data.method === "ollamaInfo") {
                    setOllamaAvailable(data.isAvailable || false);
                    setOllamaModels(data.models || []);
                }
            } catch (err) {
                console.error("Message processing error:", err);
            }
        };

        webview.addEventListener("message", handleWebviewMessage);
        return () => {
            webview.removeEventListener("message", handleWebviewMessage);
        };
    }, []);

    const requestOllamaInfo = () => {
        webview.postMessage('{ "method": "tools/call", "params": {"name": "getOllamaInfo", "arguments": {} } }');
    };

    // モデルリストを取得するヘルパー
    const getModelList = (preset: EndpointPreset): string[] => {
        if (preset === "ollama" && ollamaAvailable && ollamaModels.length > 0) {
            return ollamaModels;
        }
        return models[preset] || [];
    };

    // モデルセレクト変更ハンドラ
    const handleModelSelectChange = (value: string) => {
        if (value === CUSTOM_MODEL_VALUE) {
            setIsCustomModel(true);
            setCustomModelInput("");
        } else {
            setIsCustomModel(false);
            setModel(value);
        }
    };

    // 実際に送信・保存されるモデル名
    const effectiveModel = isCustomModel ? customModelInput : model;

    const handleSave = () => {
        setSaveError(null);

        let parsedMcpServers: Record<string, any> = {};
        try {
            parsedMcpServers = JSON.parse(mcpServersJson || "{}");
        } catch {
            setSaveError("MCP サーバー設定の JSON が不正です。修正してから保存してください。");
            setActiveTab('tab-mcp');
            return;
        }

        if (!effectiveModel.trim()) {
            setSaveError("モデル名を入力してください。");
            setActiveTab('tab-api');
            return;
        }

        const newSettings: Settings = {
            apiType,
            endpointPreset,
            apiEndpoint,
            apiKey,
            model: effectiveModel.trim(),
            azureDeployment,
            streaming,
            mcpEnabled,
            mcpServers: parsedMcpServers
        };
        localStorage.setItem("chatSettings", JSON.stringify(newSettings));

        webview.postMessage(JSON.stringify({
            method: "tools/call",
            params: {
                name: "saveSettings",
                arguments: { settingsJson: JSON.stringify(newSettings) }
            }
        }));

        webview.postMessage('{ "method": "tools/call", "params": {"name": "settingsUpdated", "arguments": {} } }');
        webview.postMessage('{ "method": "tools/call", "params": {"name": "closeSettings", "arguments": {} } }');
    };

    const handleCancel = () => {
        webview.postMessage('{ "method": "tools/call", "params": {"name": "closeSettings", "arguments": {} } }');
    };

    const closeButtonHandler = () => {
        webview.postMessage('{ "method": "tools/call", "params": {"name": "closeSettings", "arguments": {} } }');
    };

    const currentModelList = getModelList(endpointPreset);

    return (
        <Box className="window" style={{ height: '100vh' }}>
            <Box p="0" className="title-bar settings-title-bar">
                <Box className="title-bar-center">
                    <Text size="2" weight="bold" className="title-bar-text">設定</Text>
                </Box>
                <Button className="window-control-icon close-button" aria-label="Close" onClick={closeButtonHandler}>&#xEF2C;</Button>
            </Box>

            <Box className="window-body settings-body" p="3">
                {saveError && (
                    <Box mb="3" p="2" style={{ background: 'var(--red-3)', borderRadius: 'var(--radius-2)', border: '1px solid var(--red-6)' }}>
                        <Text size="2" color="red">{saveError}</Text>
                    </Box>
                )}

                <Tabs.Root value={activeTab} onValueChange={setActiveTab}>
                    <Tabs.List className="settings-tabs-list">
                        <Tabs.Trigger value="tab-api">API 設定</Tabs.Trigger>
                        <Tabs.Trigger value="tab-advanced">詳細設定</Tabs.Trigger>
                        <Tabs.Trigger value="tab-mcp">MCP 設定</Tabs.Trigger>
                    </Tabs.List>

                    <Tabs.Content mt="4" value="tab-api" style={{ minHeight: 0, overflowY: 'auto' }}>
                        {/* API 種別 */}
                        <Box className="form-group" mb="3">
                            <Text as="label" htmlFor="apiType" mb="2" weight="bold">API 種別</Text>
                            <Select.Root value={apiType} onValueChange={(value) => setApiType(value as ApiType)}>
                                <Select.Trigger id="apiType" />
                                <Select.Content>
                                    <Select.Item value="chat_completions">Chat Completions API (OpenAI 互換)</Select.Item>
                                    <Select.Item value="azure">Azure OpenAI</Select.Item>
                                    <Select.Item value="claude">Anthropic Claude API</Select.Item>
                                    <Select.Item value="gemini">Google Gemini API</Select.Item>
                                </Select.Content>
                            </Select.Root>
                            <Box mt="2">
                                <Text size="1" color="gray" className="api-type-info">{apiTypeDescriptions[apiType] || ""}</Text>
                            </Box>
                        </Box>

                        {/* エンドポイント */}
                        <Box className="form-group" mt="4">
                            <Text as="label" htmlFor="endpointPreset" mb="2" weight="bold">エンドポイント</Text>
                            <Flex gap="2" align="center" className="endpoint-group">
                                <Select.Root value={endpointPreset} onValueChange={(value) => setEndpointPreset(value as EndpointPreset)}>
                                    <Select.Trigger id="endpointPreset" style={{ flexGrow: 1 }} />
                                    <Select.Content>
                                        {Object.entries(endpoints).map(([key]) => {
                                            const compatible = compatibleEndpoints[apiType] || [];
                                            const disabled = !compatible.includes(key);
                                            if (key === "ollama" && !ollamaAvailable) return null;
                                            return (
                                                <Select.Item key={key} value={key} disabled={disabled}>
                                                    {presetDisplayNames[key] || (key.charAt(0).toUpperCase() + key.slice(1).replace('_', ' '))}
                                                </Select.Item>
                                            );
                                        })}
                                    </Select.Content>
                                </Select.Root>
                                <TextField.Root
                                    placeholder="https://api.openai.com/v1/chat/completions"
                                    value={apiEndpoint}
                                    onChange={(e) => setApiEndpoint(e.target.value)}
                                    disabled={endpointPreset !== "custom"}
                                    style={{ flexGrow: 2 }}
                                />
                            </Flex>
                        </Box>

                        {/* API キー */}
                        <Box className="form-group" mt="4">
                            <Text as="label" htmlFor="apiKey" mb="2" weight="bold">API キー</Text>
                            <TextField.Root type="password" id="apiKey" placeholder="sk-..." value={apiKey} onChange={(e) => setApiKey(e.target.value)} />
                        </Box>

                        {/* モデル */}
                        <Box className="form-group" mt="4">
                            <Text as="label" htmlFor="modelSelect" mb="2" weight="bold">モデル</Text>
                            {endpointPreset === "custom" ? (
                                // カスタムプリセット: 常にテキスト入力
                                <TextField.Root
                                    id="modelSelect"
                                    placeholder="例: gpt-4o-mini, llama3, claude-3-5-sonnet..."
                                    value={customModelInput}
                                    onChange={(e) => setCustomModelInput(e.target.value)}
                                />
                            ) : (
                                // 既知プリセット: セレクト＋「カスタム入力」オプション
                                <Flex gap="2" direction="column">
                                    <Select.Root
                                        value={isCustomModel ? CUSTOM_MODEL_VALUE : model}
                                        onValueChange={handleModelSelectChange}
                                    >
                                        <Select.Trigger id="modelSelect" />
                                        <Select.Content>
                                            {currentModelList.map(m => (
                                                <Select.Item key={m} value={m}>{m}</Select.Item>
                                            ))}
                                            <Select.Separator />
                                            <Select.Item value={CUSTOM_MODEL_VALUE}>カスタム（手入力）</Select.Item>
                                        </Select.Content>
                                    </Select.Root>
                                    {isCustomModel && (
                                        <TextField.Root
                                            placeholder="モデル名を入力..."
                                            value={customModelInput}
                                            onChange={(e) => setCustomModelInput(e.target.value)}
                                            autoFocus
                                        />
                                    )}
                                </Flex>
                            )}
                            {effectiveModel && (
                                <Text size="1" color="gray" mt="1">使用するモデル: {effectiveModel}</Text>
                            )}
                        </Box>

                        {/* Azure デプロイ名 */}
                        <Box className="form-group" mt="4" style={{ display: endpointPreset === "azure_openai" ? 'block' : 'none' }}>
                            <Text as="label" htmlFor="azureDeployment" mb="2" weight="bold">Azure OpenAI デプロイ名</Text>
                            <TextField.Root type="text" id="azureDeployment" placeholder="gpt-4o-mini" value={azureDeployment} onChange={(e) => setAzureDeployment(e.target.value)} />
                        </Box>
                    </Tabs.Content>

                    <Tabs.Content value="tab-advanced" style={{ flexGrow: 1, display: 'flex', flexDirection: 'column', minHeight: 0, overflowY: 'auto', justifyContent: 'flex-start' }}>
                        <Box mt="4" className="form-group">
                            <Flex align="center" gap="2">
                                <Switch id="streaming" checked={streaming} onCheckedChange={setStreaming} />
                                <Text as="label" htmlFor="streaming">ストリーミング応答を有効にする</Text>
                            </Flex>
                        </Box>
                        <Box className="form-group" mt="4">
                            <Flex align="center" gap="2">
                                <Switch id="mcpEnabled" checked={mcpEnabled} onCheckedChange={setMcpEnabled} />
                                <Text as="label" htmlFor="mcpEnabled">MCP (Model Context Protocol) を有効にする</Text>
                            </Flex>
                            <Text size="1" color="gray" className="api-type-info">有効にすると、AI がローカルツールを実行できるようになります。</Text>
                        </Box>
                    </Tabs.Content>

                    <Tabs.Content value="tab-mcp" style={{ flexGrow: 1, display: 'flex', flexDirection: 'column', minHeight: 0, overflowY: 'auto', justifyContent: 'flex-start' }}>
                        <Box mt="4" className="form-group">
                            <Text as="label" htmlFor="mcpServersJson" mb="1" weight="bold">MCP サーバー設定 (JSON)</Text>
                            <Text size="1" color="gray" mt="1" className="api-type-info">Claude デスクトップ互換の形式で、mcpServers プロパティのみ記述してください。</Text>
                            <TextArea
                                id="mcpServersJson"
                                rows={12}
                                style={{ width: '100%', fontFamily: 'monospace' }}
                                placeholder='{
"filesystem": {
    "command": "npx",
    "args": ["-y", "@modelcontextprotocol/server-filesystem", "C:\\Users\\user\\Desktop"]
}
}'
                                value={mcpServersJson}
                                onChange={(e) => setMcpServersJson(e.target.value)}
                            />
                        </Box>
                    </Tabs.Content>
                </Tabs.Root>

                <Flex className="form-actions" gap="2" justify="end" mt="3">
                    <Button variant="outline" onClick={handleCancel}>キャンセル</Button>
                    <Button onClick={handleSave}>保存</Button>
                </Flex>
            </Box>
        </Box>
    );
}

export default SettingsApp;