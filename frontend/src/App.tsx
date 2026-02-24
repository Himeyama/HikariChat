import { useState, useEffect, useRef } from 'react';
import { Box, Button, Tabs, TextArea, Text } from '@radix-ui/themes';
import './App.css';
import { Marked } from 'marked';
import hljs from 'highlight.js';
import { sendChatMessage, executeMcpTool, buildMessagesForNextRequest, getAvailableTools, type ToolCall, convertToOpenAITools } from './chatUtils';

// Create a new Marked instance and configure it
const customMarked = new Marked();
customMarked.use({
  renderer: {
    code({ text, lang = '' }: { text: string, lang?: string }) {
      const language = hljs.getLanguage(lang) ? lang : 'plaintext';
      const highlightedCode = hljs.highlight(text, { language, ignoreIllegals: true }).value;
      return `<pre><code class="hljs language-${language}">${highlightedCode}</code></pre>`;
    }
  }
});

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
  apiType: 'azure' | 'gemini' | 'claude' | 'chat_completions';
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
  const chatMessagesRef = useRef<HTMLDivElement>(null);
  const activeTabIdRef = useRef<string>('tab-chat-1');
  const [tabCounter, setTabCounter] = useState(1);
  const [tabs, setTabs] = useState<Record<string, ChatTab>>({
    'tab-chat-1': { conversationHistory: [], isLoading: false },
  });
  const [activeTabId, setActiveTabId] = useState('tab-chat-1');
  const [currentSettings, setCurrentSettings] = useState<Settings>(loadSettings());

  // Keep ref in sync with state
  useEffect(() => {
    activeTabIdRef.current = activeTabId;
  }, [activeTabId]);
  const [chatInput, setChatInput] = useState('');
  const [modelDisplayName, setModelDisplayName] = useState('');
  const [mcpStatus, setMcpStatus] = useState('MCP: 無効');
  const [mcpStatusClass, setMcpStatusClass] = useState('disabled'); // Corresponds to styles.css classes
  const [isMaximized, setIsMaximized] = useState(false);
  const [availableTools, setAvailableTools] = useState<any[]>([]);

  const activeTab = tabs[activeTabId];

  useEffect(() => {
    // These functions now depend on currentSettings, so they should be called when currentSettings changes
    updateModelDisplay();
    updateMcpStatusDisplay();
    requestMcpStatus();
    // loadAvailableTools() は MCP 準備完了時に mcpStatus メッセージから呼び出す

    const handleWebviewMessage = (event: any) => {
      try {
        const parsedData = JSON.parse(event.data);
        if (parsedData.method === "settingsUpdated") {
          // Use the settings directly from the message, as C# sends the current state
          setCurrentSettings(parsedData.settings);
          console.log('[Settings Updated From C#]', parsedData.settings);
          // updateModelDisplay and updateMcpStatusDisplay will be called via useEffect due to currentSettings change
        } else if (parsedData.method === "mcpStatus") {
          updateMcpStatusDisplay(parsedData);
          // MCP が準備完了したらツール一覧を取得
          if (parsedData.enabled && parsedData.activeCount === parsedData.totalCount && parsedData.totalCount > 0) {
            console.log('[MCP] MCP is ready, loading tools...');
            loadAvailableTools();
          }
        }
      } catch (e) {
        console.error("Error parsing webview message:", e, "Original message:", event.data);
      }
    };

    webview.addEventListener("message", handleWebviewMessage);

    return () => {
      webview.removeEventListener("message", handleWebviewMessage);
    };
  }, [currentSettings]); // Add currentSettings to dependency array

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
    };

    const apiTypeNames: Record<string, string> = {
      chat_completions: 'Chat Completions',
      azure: 'Azure OpenAI',
      claude: 'Claude API',
      gemini: 'Gemini API',
    };

    // カスタムエンドポイントの場合はAPI種別名を表示
    const providerName = endpointPreset === 'custom'
      ? (apiTypeNames[currentSettings.apiType] || currentSettings.apiType)
      : (presetNames[endpointPreset] || endpointPreset);

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

  const requestMcpStatus = () => {
    webview.postMessage('{ "method": "tools/call", "params": {"name": "getMcpInfo", "arguments": {} } }');
  };

  const loadAvailableTools = async () => {
    if (!currentSettings.mcpEnabled) {
      setAvailableTools([]);
      return;
    }
    try {
      const tools = await getAvailableTools();
      console.log('[loadAvailableTools] Loaded tools:', tools);
      setAvailableTools(tools);
    } catch (error) {
      console.error('[loadAvailableTools] Error:', error);
      setAvailableTools([]);
    }
  };

  const openSettingsWindow = () => {
    webview.postMessage('{ "method": "tools/call", "params": {"name": "openSettings", "arguments": {} } }');
  };

  const addMessage = (content: string, role: ChatMessage['role'], toolName?: string, toolCallId?: string) => {
    setTabs(prevTabs => {
      const message: ChatMessage = { role, content };
      if (toolName) message.name = toolName;
      if (toolCallId) message.tool_call_id = toolCallId;
      const updatedHistory = [...prevTabs[activeTabId].conversationHistory, message];
      return {
        ...prevTabs,
        [activeTabId]: { ...prevTabs[activeTabId], conversationHistory: updatedHistory }
      };
    });
  };

  /**
   * Send message to chat API
   */
  const callChatApi = async (messages: ChatMessage[]): Promise<{ content: string; toolCalls: ToolCall[] }> => {
    const useStreaming = currentSettings.streaming;
    let accumulatedContent = '';
    let messageAdded = false;

    console.log('[callChatApi] Sending messages:', JSON.stringify(messages, null, 2));

    return new Promise((resolve, reject) => {
      sendChatMessage(messages, {
        apiKey: currentSettings.apiKey,
        apiEndpoint: currentSettings.apiEndpoint,
        model: currentSettings.model,
        apiType: currentSettings.apiType,
        endpointPreset: currentSettings.endpointPreset,
        azureDeployment: currentSettings.azureDeployment,
        streaming: useStreaming,
        mcpEnabled: currentSettings.mcpEnabled
      }, {
        onContent: (content: string) => {
          accumulatedContent += content;
          // ストリーミング中に UI を更新
          if (!messageAdded) {
            addMessage('', 'assistant');
            messageAdded = true;
          }
          setTabs(prevTabs => {
            const currentTabId = activeTabIdRef.current;
            const history = [...prevTabs[currentTabId].conversationHistory];
            if (history.length > 0) {
              history[history.length - 1] = {
                role: 'assistant',
                content: accumulatedContent
              };
            }
            return {
              ...prevTabs,
              [currentTabId]: { ...prevTabs[currentTabId], conversationHistory: history }
            };
          });
        },
        onComplete: (result) => {
          resolve(result);
        },
        onError: (error) => {
          reject(error);
        }
      });
    });
  };

  /**
   * Send message to chat API with tools
   */
  const callChatApiWithTools = async (messages: ChatMessage[], tools: any[]): Promise<{ content: string; toolCalls: ToolCall[] }> => {
    const useStreaming = currentSettings.streaming;
    let accumulatedContent = '';
    let messageAdded = false;

    console.log('[callChatApiWithTools] Sending messages:', JSON.stringify(messages, null, 2));
    console.log('[callChatApiWithTools] Tools:', JSON.stringify(tools, null, 2));

    return new Promise((resolve, reject) => {
      sendChatMessage(messages, {
        apiKey: currentSettings.apiKey,
        apiEndpoint: currentSettings.apiEndpoint,
        model: currentSettings.model,
        apiType: currentSettings.apiType,
        endpointPreset: currentSettings.endpointPreset,
        azureDeployment: currentSettings.azureDeployment,
        streaming: useStreaming,
        mcpEnabled: currentSettings.mcpEnabled,
        tools: tools
      }, {
        onContent: (content: string) => {
          accumulatedContent += content;
          // ストリーミング中に UI を更新
          if (!messageAdded) {
            addMessage('', 'assistant');
            messageAdded = true;
          }
          setTabs(prevTabs => {
            const currentTabId = activeTabIdRef.current;
            const history = [...prevTabs[currentTabId].conversationHistory];
            if (history.length > 0) {
              history[history.length - 1] = {
                role: 'assistant',
                content: accumulatedContent
              };
            }
            return {
              ...prevTabs,
              [currentTabId]: { ...prevTabs[currentTabId], conversationHistory: history }
            };
          });
        },
        onComplete: (result) => {
          resolve(result);
        },
        onError: (error) => {
          reject(error);
        }
      });
    });
  };

  /**
   * Execute tool calls and return results
   */
  const executeTools = async (
    toolCalls: ToolCall[],
    executedToolCallIds: Set<string>
  ): Promise<Array<{ name: string; content: string; toolCallId: string }>> => {
    const toolResults: Array<{ name: string; content: string; toolCallId: string }> = [];

    for (const tc of toolCalls) {
      // Skip already executed tool calls
      if (executedToolCallIds.has(tc.id)) {
        console.log(`[executeTools] Skipping already executed tool: ${tc.id}`);
        continue;
      }

      console.log(`[executeTools] Executing tool: ${tc.name}`, tc.arguments);

      let args = {};
      try {
        args = JSON.parse(tc.arguments);
      } catch (e) {
        console.error("[executeTools] Error parsing tool arguments:", e);
      }

      // Execute the tool
      const result = await executeMcpTool(tc.name, args);
      const resultString = JSON.stringify(result);

      console.log(`[executeTools] Tool result:`, result);

      // Add tool result message to UI
      addMessage(resultString, "tool", tc.name, tc.id);

      toolResults.push({
        name: tc.name,
        content: resultString,
        toolCallId: tc.id
      });

      executedToolCallIds.add(tc.id);
    }

    return toolResults;
  };

  /**
   * Recursive function to handle chat with tool calls
   */
  const processChatRecursive = async (
    localMessages: ChatMessage[],
    executedToolCallIds: Set<string>,
    iterationCount: number,
    maxIterations: number
  ): Promise<void> => {
    // Base case: max iterations reached
    if (iterationCount >= maxIterations) {
      return;
    }

    // Send message to chat API
    const result = await callChatApi(localMessages);

    // アシスタントメッセージをローカルメッセージに追加
    // 非ストリーミング時は UI にも追加（ストリーミング時は UI 更新済み）
    if (result.content || result.toolCalls.length > 0) {
      if (!currentSettings.streaming) {
        addMessage(result.content, "assistant");
      }
      localMessages.push({ role: "assistant", content: result.content });
    }

    // If no tool calls, we're done
    if (result.toolCalls.length === 0) {
      return;
    }

    // Execute tools and get results
    const toolResults = await executeTools(result.toolCalls, executedToolCallIds);

    // If no new tools were executed, stop
    if (toolResults.length === 0) {
      return;
    }

    // Build messages for next request with tool results
    const nextMessages = buildMessagesForNextRequest(
      localMessages,
      result.content,
      result.toolCalls,
      toolResults
    );

    // Recursive call for next iteration
    await processChatRecursive(nextMessages, executedToolCallIds, iterationCount + 1, maxIterations);
  };

  /**
   * Recursive function to handle chat with tool calls (with OpenAI tools)
   */
  const processChatRecursiveWithTools = async (
    localMessages: ChatMessage[],
    executedToolCallIds: Set<string>,
    iterationCount: number,
    maxIterations: number,
    tools: any[]
  ): Promise<void> => {
    // Base case: max iterations reached
    if (iterationCount >= maxIterations) {
      console.log('[processChatRecursiveWithTools] Max iterations reached');
      return;
    }

    console.log(`[processChatRecursiveWithTools] Iteration ${iterationCount}, messages:`, localMessages);

    // Send message to chat API with tools
    const result = await callChatApiWithTools(localMessages, tools);

    console.log('[processChatRecursiveWithTools] LLM result:', result);

    // アシスタントメッセージをローカルメッセージに追加
    if (result.content || result.toolCalls.length > 0) {
      if (!currentSettings.streaming) {
        addMessage(result.content, "assistant");
      }
      localMessages.push({ role: "assistant", content: result.content });
    }

    // If no tool calls, we're done
    if (result.toolCalls.length === 0) {
      console.log('[processChatRecursiveWithTools] No tool calls, done');
      return;
    }

    console.log('[processChatRecursiveWithTools] Executing tools:', result.toolCalls);

    // Execute tools and get results
    const toolResults = await executeTools(result.toolCalls, executedToolCallIds);

    console.log('[processChatRecursiveWithTools] Tool results:', toolResults);

    // If no new tools were executed, stop
    if (toolResults.length === 0) {
      console.log('[processChatRecursiveWithTools] No tools executed, done');
      return;
    }

    // Build messages for next request with tool results
    const nextMessages = buildMessagesForNextRequest(
      localMessages,
      result.content,
      result.toolCalls,
      toolResults
    );

    // Recursive call for next iteration
    await processChatRecursiveWithTools(nextMessages, executedToolCallIds, iterationCount + 1, maxIterations, tools);
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
    const userMessage: ChatMessage = { role: "user", content: messageToSend };

    // Build messages with user message only (tools are passed separately to API)
    const localMessages: ChatMessage[] = [userMessage];
    
    // Convert MCP tools to OpenAI format
    const openaiTools = convertToOpenAITools(availableTools);
    
    // Add user message to UI
    addMessage(messageToSend, "user");
    setChatInput('');

    try {
      const executedToolCallIds = new Set<string>();
      const MAX_ITERATIONS = 5;

      // Start recursive chat processing with tools
      await processChatRecursiveWithTools(localMessages, executedToolCallIds, 0, MAX_ITERATIONS, openaiTools);
    } catch (error: any) {
      addMessage(error.message, "error");
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
          <Button className="window-control-icon" aria-label="Minimize" onClick={() => webview.postMessage('{ "method": "tools/call", "params": {"name": "control", "arguments": {"command": "minimize" }} }')}>&#xEF2D;</Button>
          <Button className="window-control-icon" aria-label={isMaximized ? "Restore" : "Maximize"} onClick={() => {
              setIsMaximized(prev => !prev);
              webview.postMessage('{ "method": "tools/call", "params": {"name": "control", "arguments": {"command": "toggleMaximize" }} }');
            }}>{isMaximized ? '\uEF2F' : '\uEF2E'}</Button>
          <Button className="window-control-icon close-button" aria-label="Close" onClick={() => webview.postMessage('{ "method": "tools/call", "params": {"name": "control", "arguments": {"command": "close" }} }')}>&#xEF2C;</Button>
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
                        dangerouslySetInnerHTML={{ __html: message.role === 'assistant' ? customMarked.parse(message.content) : message.content }}
                      />
                    </Box>
                  ))
                )}
              </Box>
              <Box
                className="chat-input-area"
                pt="4"
              >
                <TextArea
                  placeholder="メッセージを入力..."
                  rows={3}
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
                >&#xE74A;</Button>
              </Box>
            </Tabs.Content>
          ))}
        </Tabs.Root>
      </Box>
    </Box>
  );
}

export default App;