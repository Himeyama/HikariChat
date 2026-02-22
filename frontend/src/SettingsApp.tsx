import { useState, useEffect } from 'react';
import { Box, Button, Flex, Tabs, TextField, Select, Switch, TextArea, Text } from '@radix-ui/themes';
import '@radix-ui/themes/styles.css'; // Ensure Radix styles are loaded
import './App.css'; // Reuse general styles like .window, .title-bar etc.

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

// Helper function to load settings (copied from App.tsx or similar logic)
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

type ApiType = 'chat_completions' | 'responses' | 'anthropic' | 'gemini';
type EndpointPreset = 'openai' | 'azure_openai' | 'gemini' | 'grok' | 'anthropic' | 'ollama' | 'custom';

const compatibleEndpoints = {
    chat_completions: ["openai", "azure_openai", "grok", "ollama", "custom"],
    responses: ["openai", "custom"],
    anthropic: ["anthropic", "custom"],
    gemini: ["gemini", "custom"]
};

const apiTypeDescriptions: Record<ApiType, string> = {
    chat_completions: "OpenAI 互換の API エンドポイントを使用します",
    responses: "OpenAI Responses API を使用します",
    anthropic: "Anthropic Claude API を使用します",
    gemini: "Google Gemini API を使用します"
};

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
    ollama: [ // This list is dynamic from webview, providing a default list
        "llama3.3", "llama3.2", "qwen3", "qwen2.5", "deepseek-r1",
        "mistral", "gemma2", "phi4",
    ],
    custom: []
};

function SettingsApp() {
    const initialSettings = loadSettings();
    const [ollamaAvailable, setOllamaAvailable] = useState(false);
    const [ollamaModels, setOllamaModels] = useState<string[]>([]);
    const [apiTypeInfoText, setApiTypeInfoText] = useState(apiTypeDescriptions[initialSettings.apiType as ApiType]);
    const [activeTab, setActiveTab] = useState('tab-api');

    // State for form fields
    const [apiType, setApiType] = useState<ApiType>(initialSettings.apiType as ApiType);
    const [endpointPreset, setEndpointPreset] = useState<EndpointPreset>(initialSettings.endpointPreset as EndpointPreset);
    const [apiEndpoint, setApiEndpoint] = useState(initialSettings.apiEndpoint);
    const [apiKey, setApiKey] = useState(initialSettings.apiKey);
    const [model, setModel] = useState(initialSettings.model);
    const [azureDeployment, setAzureDeployment] = useState(initialSettings.azureDeployment);
    const [streaming, setStreaming] = useState(initialSettings.streaming);
    const [mcpEnabled, setMcpEnabled] = useState(initialSettings.mcpEnabled);
    const [mcpServersJson, setMcpServersJson] = useState(JSON.stringify(initialSettings.mcpServers || {}, null, 2));

    useEffect(() => {
        // Initial setup
        updateEndpointOptions(apiType);
        updateModelList(endpointPreset, model);
        requestOllamaInfo();

        const handleWebviewMessage = (event: any) => {
            try {
                const data = JSON.parse(event.data);
                if (data.method === "ollamaInfo") {
                    setOllamaAvailable(data.isAvailable || false);
                    setOllamaModels(data.models || []);
                    console.log("Ollama info updated:", data.isAvailable, data.models);

                    // Re-evaluate endpoint and model lists based on new Ollama info
                    updateEndpointOptions(apiType);
                    if (endpointPreset === "ollama") {
                        updateModelList("ollama", model); // Pass current model to try to select it
                    }
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

    useEffect(() => {
        setApiTypeInfoText(apiTypeDescriptions[apiType] || "");
        updateEndpointOptions(apiType);
        updateEndpoint(); // Call updateEndpoint explicitly
    }, [apiType]);

    useEffect(() => {
        updateEndpoint(); // Call updateEndpoint explicitly
    }, [endpointPreset, apiType]); // Re-evaluate when preset or apiType changes

    const requestOllamaInfo = () => {
        webview.postMessage('{ "method": "tools/call", "params": {"name": "getOllamaInfo", "arguments": {} } }');
    };

    const updateEndpoint = () => {
        const compatible = compatibleEndpoints[apiType] || [];
        if (!compatible.includes(endpointPreset)) {
            setEndpointPreset("custom"); // Fallback to custom if not compatible
        }

        if (endpointPreset === "custom") {
            // Keep current apiEndpoint for custom
        } else {
            const endpoint = endpoints[endpointPreset]?.[apiType] || "";
            setApiEndpoint(endpoint);
        }
    };

    const updateEndpointOptions = (currentApiType: ApiType) => {
        const compatible = compatibleEndpoints[currentApiType] || [];
        // No direct DOM manipulation here, need to filter options in JSX
        // This function primarily ensures endpointPreset is compatible
        if (!compatible.includes(endpointPreset)) {
            setEndpointPreset("custom");
        }
    };

    const updateModelList = (currentPreset: string, targetModel: string | null = null) => {
        let modelOptions: string[] = [];

        if (currentPreset === "ollama" && ollamaAvailable && ollamaModels.length > 0) {
            modelOptions = ollamaModels;
        } else {
            modelOptions = models[currentPreset] || models.custom;
        }

        // Set model state, trying to preserve if still in list
        if (targetModel && modelOptions.includes(targetModel)) {
            setModel(targetModel);
        } else if (!modelOptions.includes(model)) {
            setModel(modelOptions[0] || ""); // Default to first available
        }
    };

    const handleSave = () => {
        const newSettings: Settings = {
            apiType: apiType,
            endpointPreset: endpointPreset,
            apiEndpoint: apiEndpoint,
            apiKey: apiKey,
            model: model,
            azureDeployment: azureDeployment,
            streaming: streaming,
            mcpEnabled: mcpEnabled,
            mcpServers: JSON.parse(mcpServersJson || "{}")
        };
        localStorage.setItem("chatSettings", JSON.stringify(newSettings));

        webview.postMessage(JSON.stringify({
            method: "tools/call",
            params: {
                name: "saveSettings",
                arguments: {
                    settingsJson: JSON.stringify(newSettings)
                }
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

    return (
        <Box className="window" style={{ height: '100vh' }}>
            <Flex p="0" className="title-bar" align="center" justify="between">
                <Flex pl="2">
                    <Text size="2" weight="bold" className="title-bar-text">設定</Text>
                </Flex>
                <Flex gap="1">
                    <Button className="window-control-icon close-button" aria-label="Close" size="1" onClick={closeButtonHandler}>&#xE8BB;</Button>
                </Flex>
            </Flex>

            <Box className="window-body settings-body" p="3">
                <Tabs.Root value={activeTab} onValueChange={setActiveTab}>
                    <Tabs.List className="settings-tabs-list">
                        <Tabs.Trigger value="tab-api">API 設定</Tabs.Trigger>
                        <Tabs.Trigger value="tab-advanced">詳細設定</Tabs.Trigger>
                        <Tabs.Trigger value="tab-mcp">MCP 設定</Tabs.Trigger>
                    </Tabs.List>

                    <Tabs.Content mt="4" value="tab-api" style={{ minHeight: 0, overflowY: 'auto' }}>
                        <Box className="form-group" mb="3">
                            <Text as="label" htmlFor="apiType" mb="2" weight="bold">API 種別</Text>
                            <Select.Root value={apiType} onValueChange={(value) => setApiType(value as ApiType)}>
                                <Select.Trigger id="apiType" />
                                <Select.Content>
                                    <Select.Item value="chat_completions">Chat Completions API (OpenAI 互換)</Select.Item>
                                    <Select.Item value="responses">Responses API (OpenAI)</Select.Item>
                                    <Select.Item value="anthropic">Anthropic API</Select.Item>
                                    <Select.Item value="gemini">Google Gemini API</Select.Item>
                                </Select.Content>
                            </Select.Root>
                            <Box mt="2">
                                <Text size="1" color="gray" className="api-type-info">{apiTypeInfoText}</Text>
                            </Box>
                        </Box>
                        <Box className="form-group" mt="4">
                            <Text as="label" htmlFor="endpointPreset" mb="2" weight="bold">エンドポイント</Text>
                            <Flex gap="2" align="center" className="endpoint-group">
                                <Select.Root value={endpointPreset} onValueChange={(value) => setEndpointPreset(value as EndpointPreset)}>
                                    <Select.Trigger id="endpointPreset" style={{ flexGrow: 1 }} />
                                    <Select.Content>
                                        {Object.entries(endpoints).map(([key, _]) => {
                                            const compatible = compatibleEndpoints[apiType] || [];
                                            const disabled = !compatible.includes(key) && key !== endpointPreset; // Keep selected if not compatible
                                            if (key === "ollama" && !ollamaAvailable && !disabled) return null; // Don't show Ollama if not available

                                            return (
                                                <Select.Item key={key} value={key} disabled={disabled}>
                                                    {key.charAt(0).toUpperCase() + key.slice(1).replace('_', ' ')}
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
                        <Box className="form-group" mt="4">
                            <Text as="label" htmlFor="apiKey" mb="2" weight="bold">API キー</Text>
                            <TextField.Root type="password" id="apiKey" placeholder="sk-..." value={apiKey} onChange={(e) => setApiKey(e.target.value)} />
                        </Box>
                        <Box className="form-group" mt="4">
                            <Text as="label" htmlFor="modelSelect" mb="2" weight="bold">モデル</Text>
                            <Select.Root value={model} onValueChange={setModel}>
                                <Select.Trigger id="modelSelect" />
                                <Select.Content>
                                    {(endpointPreset === "ollama" && ollamaAvailable && ollamaModels.length > 0 ? ollamaModels : models[endpointPreset] || models.custom).map(m => (
                                        <Select.Item key={m} value={m}>{m}</Select.Item>
                                    ))}
                                </Select.Content>
                            </Select.Root>
                        </Box>
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
    "args": ["-y", "@modelcontextprotocol/server-filesystem", "C:\Users\user\Desktop"]
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
