import { useState, useEffect, useRef } from 'react';
import { Box, Button, Tabs, TextArea, Text } from '@radix-ui/themes';
import './App.css';
import { marked } from 'marked';

// Mock WebView2 communication for now
const mockWebView2 = {
  postMessage: (message: string) => console.log('WebView2 Post Message:', message),
  addEventListener: (_event: string, _callback: (e: any) => void) => {
    // Mock for settingsUpdated
    // setTimeout(() => callback({ data: 'settingsUpdated' }), 3000);
  },
  removeEventListener: (_event: string, _callback: (e: any) => void) => {},
};

// Use mock or actual webview
const webview = (window as any).chrome?.webview || mockWebView2;

interface ChatMessage {
  role: 'user' | 'assistant' | 'system' | 'error' | 'tool';
  content: string;
  tool_call_id?: string;
  name?: string;
  tool_calls?: any[];
}

interface ChatTab {
  conversationHistory: ChatMessage[];
  isLoading: boolean;
}

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



function App() {
  const chatMessagesRef = useRef<HTMLDivElement>(null); // Added this line

  const [tabCounter, setTabCounter] = useState(1);
  const [tabs, setTabs] = useState<Record<string, ChatTab>>({
    'tab-chat-1': { conversationHistory: [], isLoading: false },
  });
  const [activeTabId, setActiveTabId] = useState('tab-chat-1');
  const [currentSettings, setCurrentSettings] = useState<Settings>(loadSettings());
  const [chatInput, setChatInput] = useState('');
  const [modelDisplayName, setModelDisplayName] = useState('');
  const [mcpStatus, setMcpStatus] = useState('MCP: 無効');
  const [mcpStatusClass, setMcpStatusClass] = useState('disabled'); // Corresponds to styles.css classes
  const [isMaximized, setIsMaximized] = useState(false);

  const activeTab = tabs[activeTabId];

  useEffect(() => {
    updateModelDisplay();
    updateMcpStatusDisplay();
    requestMcpStatus();

    const handleWebviewMessage = (event: any) => {
      if (event.data === "settingsUpdated") {
        refreshSettings();
      } else {
        try {
          const data = JSON.parse(event.data);
          if (data.method === "mcpStatus") {
            updateMcpStatusDisplay(data);
          }
        } catch (e) {
          console.error("Error parsing webview message:", e);
        }
      }
    };

    webview.addEventListener("message", handleWebviewMessage);

    return () => {
      webview.removeEventListener("message", handleWebviewMessage);
    };
  }, []);

  useEffect(() => {
    updateSendButtonState();
  }, [chatInput, activeTab?.isLoading, currentSettings.apiKey, currentSettings.endpointPreset]);

  const updateModelDisplay = () => {
    const model = currentSettings.model || 'モデル未設定';
    const endpointPreset = currentSettings.endpointPreset || 'unknown';

    const presetNames: Record<string, string> = {
      openai: 'OpenAI',
      azure_openai: 'Azure OpenAI',
      gemini: 'Google Gemini',
      grok: 'Grok (xAI)',
      anthropic: 'Anthropic',
      ollama: 'Ollama',
      custom: 'Custom'
    };

    const providerName = presetNames[endpointPreset] || endpointPreset;
    setModelDisplayName(`${providerName} / ${model}`);
  };

  const updateMcpStatusDisplay = (status?: any) => {
    let newStatusText = "";
    let newStatusClass = "";

    if (!status) {
      if (!currentSettings.mcpEnabled) {
        newStatusText = "MCP: 無効";
        newStatusClass = "disabled";
      } else {
        newStatusText = "MCP: 準備中...";
        newStatusClass = "not-ready";
      }
    } else {
      if (!status.enabled) {
        newStatusText = "MCP: 無効";
        newStatusClass = "disabled";
      } else if (status.totalCount > 0 && status.activeCount === status.totalCount) {
        newStatusText = `MCP: 準備完了 (${status.activeCount} サーバー)`;
        newStatusClass = "ready";
      } else if (status.totalCount === 0) {
        newStatusText = "MCP: サーバー未登録";
        newStatusClass = "not-ready";
      } else {
        newStatusText = `MCP: 未準備 (${status.activeCount}/${status.totalCount})`;
        newStatusClass = "not-ready";
      }
    }
    setMcpStatus(newStatusText);
    setMcpStatusClass(newStatusClass);
  };

  const refreshSettings = () => {
    const newSettings = loadSettings();
    setCurrentSettings(newSettings);
    console.log('[Settings Updated]', newSettings);
    updateModelDisplay();
    updateMcpStatusDisplay();
    requestMcpStatus();
  };

  const requestMcpStatus = () => {
    webview.postMessage('{ "method": "tools/call", "params": {"name": "getMcpInfo", "arguments": {} } }');
  };

  const openSettingsWindow = () => {
    webview.postMessage('{ "method": "tools/call", "params": {"name": "openSettings", "arguments": {} } }');
  };

  const addMessage = (content: string, role: ChatMessage['role']) => {
    setTabs(prevTabs => {
      const updatedHistory = [...prevTabs[activeTabId].conversationHistory, { role, content }];
      return {
        ...prevTabs,
        [activeTabId]: { ...prevTabs[activeTabId], conversationHistory: updatedHistory }
      };
    });
  };

  const updateAssistantMessage = (index: number, content: string) => {
    setTabs(prevTabs => {
      const updatedHistory = [...prevTabs[activeTabId].conversationHistory];
      if (updatedHistory[index]) {
        updatedHistory[index] = { ...updatedHistory[index], content };
      }
      return {
        ...prevTabs,
        [activeTabId]: { ...prevTabs[activeTabId], conversationHistory: updatedHistory }
      };
    });
  };

  const executeMcpTool = async (name: string, args: any) => {
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
    } catch (e: any) {
      return { error: e.message };
    }
  };

  const sendMessage = async () => {
    if (!chatInput.trim() || activeTab.isLoading) return;

    if (!currentSettings.apiKey && currentSettings.endpointPreset !== "ollama") {
      addMessage("API キーが設定されていません。設定から入力してください。", "error");
      openSettingsWindow();
      return;
    }

    setTabs(prevTabs => ({
      ...prevTabs,
      [activeTabId]: { ...prevTabs[activeTabId], isLoading: true }
    }));

    const messageToSend = chatInput.trim();
    addMessage(messageToSend, "user");
    setChatInput('');

    let conversationHistory = [...activeTab.conversationHistory, { role: "user", content: messageToSend }];

    try {
      let continueLoop = true;
      let currentAssistantMessageIndex: number | null = null; // To update streaming assistant messages

      while (continueLoop) {
        continueLoop = false;
        let assistantContent = "";
        let toolCalls: any[] = [];

        if (currentSettings.streaming) {
          const response = await fetch("/api/chat", {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify({
              messages: conversationHistory,
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

          const reader = response.body!.getReader();
          const decoder = new TextDecoder();
          let buffer = "";

          // Add a placeholder for the assistant's streaming response
          setTabs(prevTabs => {
            const history = [...prevTabs[activeTabId].conversationHistory, { role: 'assistant', content: '' } as ChatMessage];
            currentAssistantMessageIndex = history.length - 1;
            return {
              ...prevTabs,
              [activeTabId]: { ...prevTabs[activeTabId], conversationHistory: history }
            };
          });


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

                  if (parsed.info) {
                    addMessage(parsed.info, "system");
                    assistantContent = ""; // Reset assistant content for next part
                    // Create new placeholder for subsequent assistant message
                    setTabs(prevTabs => {
                      const history = [...prevTabs[activeTabId].conversationHistory, { role: 'assistant', content: '' } as ChatMessage];
                      currentAssistantMessageIndex = history.length - 1;
                      return {
                        ...prevTabs,
                        [activeTabId]: { ...prevTabs[activeTabId], conversationHistory: history }
                      };
                    });
                    continue;
                  }

                  const delta = parsed.choices?.[0]?.delta;

                  if (delta) {
                    if (delta.content && currentAssistantMessageIndex !== null) {
                      assistantContent += delta.content;
                      updateAssistantMessage(currentAssistantMessageIndex, assistantContent);
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
                } catch (e) {
                  console.error("Error parsing streaming data:", e);
                }
              }
            }
          }
        } else {
          // Non-streaming
          const response = await fetch("/api/chat", {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify({
              messages: conversationHistory,
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
              toolCalls = message.tool_calls.map((tc: any) => ({
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

        // Add assistant message to history if it's not a streaming update
        if (currentSettings.streaming && currentAssistantMessageIndex !== null) {
          conversationHistory[currentAssistantMessageIndex] = { role: 'assistant', content: assistantContent };
        } else if (!currentSettings.streaming && assistantContent) {
            // Already added in non-streaming branch, but need to update conversationHistory
            conversationHistory.push({ role: 'assistant', content: assistantContent });
        }


        const assistantMsg: ChatMessage & { tool_calls?: any[] } = { role: "assistant", content: assistantContent };
        if (toolCalls.length > 0) {
          assistantMsg.tool_calls = toolCalls.map(tc => ({
            id: tc.id,
            type: "function",
            function: { name: tc.name, arguments: tc.arguments }
          }));
        }
        conversationHistory = [...conversationHistory, assistantMsg];


        // Tool execution
        if (toolCalls.length > 0) {
          // If streaming, ensure last message is updated for tool execution
          if (currentSettings.streaming && currentAssistantMessageIndex !== null) {
            updateAssistantMessage(currentAssistantMessageIndex, `${assistantContent}\n\n> ツール実行中...`);
          } else {
            addMessage(`${assistantContent}\n\n> ツール実行中...`, "assistant");
            currentAssistantMessageIndex = conversationHistory.length -1
          }


          for (const tc of toolCalls.filter(Boolean)) { // Filter out any null/undefined entries
            let args = {};
            try { args = JSON.parse(tc.arguments); } catch (e) { console.error("Error parsing tool arguments:", e); }

            const result = await executeMcpTool(tc.name, args);
            const resultString = JSON.stringify(result);

            const toolMessage = {
              role: "tool" as const,
              tool_call_id: tc.id,
              name: tc.name,
              content: resultString
            };
            conversationHistory.push(toolMessage);
            
            // Update the last assistant message with tool execution status
            if (currentAssistantMessageIndex !== null) {
                updateAssistantMessage(currentAssistantMessageIndex, `${assistantContent}\n\n> ツール \`${tc.name}\` の実行が完了しました。`);
            }
          }
          continueLoop = true; // Call AI again after tool execution
        }
      }
    } catch (error: any) {
      addMessage(error.message, "error");
      console.error("Chat error:", error);
    } finally {
      setTabs(prevTabs => ({
        ...prevTabs,
        [activeTabId]: { ...prevTabs[activeTabId], isLoading: false }
      }));
    }
  };

  const addTab = () => {
    setTabCounter(prev => prev + 1);
    const newTabId = `tab-chat-${tabCounter + 1}`;
    setTabs(prevTabs => ({
      ...prevTabs,
      [newTabId]: { conversationHistory: [], isLoading: false }
    }));
    setActiveTabId(newTabId);
  };

  const switchTab = (tabId: string) => {
    setActiveTabId(tabId);
  };

  const closeTab = (tabId: string) => {
    const tabKeys = Object.keys(tabs);
    if (tabKeys.length === 1) { // If it's the last tab
      webview.postMessage('{ "method": "tools/call", "params": {"name": "control", "arguments": {"command": "close" }} }');
      return;
    }

    setTabs(prevTabs => {
      const newTabs = { ...prevTabs };
      delete newTabs[tabId];

      if (activeTabId === tabId) {
        const remainingTabIds = Object.keys(newTabs);
        const currentIndex = tabKeys.indexOf(tabId);
        const newActiveIndex = currentIndex > 0 ? currentIndex - 1 : 0;
        setActiveTabId(remainingTabIds[newActiveIndex]);
      }
      return newTabs;
    });
  };

  const resetChat = () => {
    setTabs(prevTabs => ({
      ...prevTabs,
      [activeTabId]: { conversationHistory: [], isLoading: false }
    }));
  };

  const updateSendButtonState = () => {
    // This is implicitly handled by `sendMessage`'s early return and button disabled state
  };

  useEffect(() => {
    if (chatMessagesRef.current && tabs[activeTabId].conversationHistory.length > 0) {
      const chatMessagesElement = chatMessagesRef.current;
      const lastMessage = tabs[activeTabId].conversationHistory[tabs[activeTabId].conversationHistory.length - 1];

      // Use a timeout to ensure DOM has updated with new messages
      setTimeout(() => {
        if (lastMessage.role === 'user') {
          // Find the last user message DOM element
          const lastUserMessageElement = chatMessagesElement.querySelector('.chat-message.user:last-child');
          if (lastUserMessageElement) {
            chatMessagesElement.scrollTop = (lastUserMessageElement as HTMLElement).offsetTop;
          }
        } else {
            // For assistant messages, or if the user specifically wants to scroll to the very bottom
            // We can keep the always scroll to bottom behaviour here for other messages.
            chatMessagesElement.scrollTop = chatMessagesElement.scrollHeight;
        }
      }, 0);
    }
  }, [tabs[activeTabId].conversationHistory, activeTabId]); // Trigger when conversation history or active tab changes

  return (
    <Box className="window">
      <Box className="title-bar" p="0">
        <Box p="2">
          <Text weight="bold" className="title-bar-text">ひかりチャット</Text>
        </Box>
        <Box className="title-bar-controls">
          <Button className="window-control-icon" aria-label="Minimize" onClick={() => webview.postMessage('{ "method": "tools/call", "params": {"name": "control", "arguments": {"command": "minimize" }} }')}>&#xE921;</Button>
          <Button className="window-control-icon" aria-label={isMaximized ? "Restore" : "Maximize"} onClick={() => {
              setIsMaximized(prev => !prev);
              webview.postMessage('{ "method": "tools/call", "params": {"name": "control", "arguments": {"command": "toggleMaximize" }} }');
            }}>{isMaximized ? '\uE923' : '\uE922'}</Button>
          <Button className="window-control-icon close-button" aria-label="Close" onClick={() => webview.postMessage('{ "method": "tools/call", "params": {"name": "control", "arguments": {"command": "close" }} }')}>&#xE8BB;</Button>
        </Box>
      </Box>

      <Box className="window-body" p="3">
        <Box className="chat-header" mb="3">
          <Text className="model-display">{modelDisplayName}</Text>
          <Box />
          <Text className={`mcp-status-display ${mcpStatusClass}`}>{mcpStatus}</Text>
          <Button onClick={addTab} className="add-tab-button" title="新しいタブを追加">＋</Button>
          <Button onClick={resetChat} className="reset-button" title="チャットをリセット">リセット</Button>
          <Button onClick={openSettingsWindow} className="settings-button" title="設定">設定</Button>
        </Box>

        <Tabs.Root value={activeTabId} onValueChange={switchTab} className="chat-tabs-root">
          <Tabs.List className="chat-tabs-list">
            {Object.entries(tabs).map(([tabId, _tab]) => (
              <Tabs.Trigger value={tabId} key={tabId}>
                <Box className="chat-tab-trigger-content">
                  <Text>{`チャット ${tabId.split('-').pop()}`}</Text>
                    <Text ml="4" size="1" className="tab-close-button" onClick={(e) => { e.stopPropagation(); closeTab(tabId); }}>&#xE8BB;</Text>
                </Box>
              </Tabs.Trigger>
            ))}
          </Tabs.List>

          {Object.entries(tabs).map(([tabId, tab]) => (
            <Tabs.Content value={tabId} key={tabId} className="chat-tab-content">
              <Box className="chat-messages" p="3" ref={chatMessagesRef}>
                {tab.conversationHistory.length === 0 ? (
                  <Text color="gray" size="2" style={{ textAlign: 'center', display: 'block', padding: 'var(--space-5)' }}>
                    AI とのチャットを開始しましょう
                  </Text>
                ) : (
                  tab.conversationHistory.map((message, index) => (
                    <Box key={index} mb="2" p="3" className={`chat-message ${message.role}`}>
                      <Box
                        className="message-content"
                        dangerouslySetInnerHTML={{ __html: message.role === 'assistant' ? marked.parse(message.content) : message.content }}
                      />
                    </Box>
                  ))
                )}
              </Box>
              <Box
                className="chat-input-area"
                pt="4"
                style={{
                  borderTop: '1px solid var(--gray-6)',
                  display: 'grid',
                  gridTemplateColumns: '1fr auto', // TextArea takes remaining space, Button takes auto width
                  gap: 'var(--space-2)', // Equivalent to gap="2"
                }}
              >
                <TextArea
                  placeholder="メッセージを入力..."
                  rows={3}
                  style={{
                    // flexGrow: 1 is no longer needed directly on TextArea, as grid handles sizing
                  }}
                  value={chatInput}
                  onChange={(e) => setChatInput(e.target.value)}
                  onKeyDown={(e) => {
                    if (e.key === 'Enter' && !e.shiftKey) {
                      e.preventDefault();
                      sendMessage();
                    }
                  }}
                  disabled={activeTab.isLoading}
                />
                <Button
                  size="3"
                  onClick={sendMessage}
                  disabled={!chatInput.trim() || activeTab.isLoading}
                >
                  送信
                </Button>
              </Box>
            </Tabs.Content>
          ))}
        </Tabs.Root>
      </Box>

      {/* <ThemePanel /> */}
    </Box>
  );
}

export default App;
