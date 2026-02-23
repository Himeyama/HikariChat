import OpenAI from 'openai';
import Anthropic from '@anthropic-ai/sdk';
import { GoogleGenerativeAI } from '@google/generative-ai';
import { AzureOpenAI } from 'openai';

export interface ChatMessage {
  role: 'user' | 'assistant' | 'system' | 'tool' | 'error';
  content: string;
  name?: string;
  tool_call_id?: string;
  tool_calls?: Array<{
    id: string;
    type: 'function';
    function: {
      name: string;
      arguments: string;
    };
  }>;
}

export interface ToolCall {
  id: string;
  name: string;
  arguments: string;
}

export interface SendMessageOptions {
  apiKey: string;
  apiEndpoint: string;
  model: string;
  apiType: string;
  endpointPreset: string;
  azureDeployment: string;
  streaming: boolean;
  mcpEnabled: boolean;
}

export interface SendMessageResult {
  content: string;
  toolCalls: ToolCall[];
}

export interface StreamCallbacks {
  onContent?: (content: string) => void;
  onComplete?: (result: SendMessageResult) => void;
  onError?: (error: Error) => void;
}

/**
 * Convert internal ChatMessage to OpenAI format
 */
function toOpenAIMessages(messages: ChatMessage[]): OpenAI.Chat.ChatCompletionMessageParam[] {
  return messages.map(msg => ({
    role: msg.role === 'error' ? 'user' : msg.role,
    content: msg.content,
    ...(msg.name && { name: msg.name }),
    ...(msg.tool_call_id && { tool_call_id: msg.tool_call_id })
  })) as OpenAI.Chat.ChatCompletionMessageParam[];
}

/**
 * Convert internal ChatMessage to Anthropic format
 */
function toAnthropicMessages(messages: ChatMessage[]): Anthropic.MessageParam[] {
  return messages
    .filter(msg => msg.role !== 'system') // System messages handled separately
    .map(msg => ({
      role: msg.role === 'assistant' ? 'assistant' : 'user',
      content: msg.content
    }));
}

/**
 * Send message to OpenAI API
 */
async function sendToOpenAI(
  messages: ChatMessage[],
  options: SendMessageOptions,
  callbacks?: StreamCallbacks
): Promise<SendMessageResult> {
  const openai = new OpenAI({
    apiKey: options.apiKey,
    baseURL: options.endpointPreset === 'custom' ? options.apiEndpoint.replace('/chat/completions', '') : undefined,
    dangerouslyAllowBrowser: true
  });

  if (options.streaming && callbacks?.onContent) {
    // ストリーミング処理
    const stream = await openai.chat.completions.create({
      model: options.model,
      messages: toOpenAIMessages(messages),
      stream: true
    });

    let fullContent = '';
    const toolCallsMap = new Map<string, { id: string; name: string; arguments: string }>();

    for await (const chunk of stream) {
      const delta = chunk.choices[0]?.delta;

      if (delta?.content) {
        fullContent += delta.content;
        callbacks.onContent?.(delta.content);
      }

      // ツール呼び出しの処理
      if (delta?.tool_calls) {
        for (const tc of delta.tool_calls) {
          const key = tc.index?.toString() || tc.id;
          if (key && !toolCallsMap.has(key)) {
            toolCallsMap.set(key, {
              id: tc.id || '',
              name: tc.function?.name || '',
              arguments: ''
            });
          }
          if (tc.function) {
            const existingToolCall = toolCallsMap.get(key);
            if (existingToolCall) {
              existingToolCall.arguments += (tc.function.arguments || '') as string;
            }
          }
        }
      }
    }

    const toolCalls = Array.from(toolCallsMap.values()).filter(tc => tc.id && tc.name && tc.arguments);

    const result = {
      content: fullContent,
      toolCalls
    };

    callbacks.onComplete?.(result);
    return result;
  } else {
    // 非ストリーミング処理
    console.log('[sendToOpenAI] calling non-streaming API...');
    const response = await openai.chat.completions.create({
      model: options.model,
      messages: toOpenAIMessages(messages)
    });

    const message = response.choices[0]?.message;
    const result = {
      content: message?.content || "",
      toolCalls: message?.tool_calls
        ?.filter(tc => tc.type === 'function')
        .map(tc => ({
          id: tc.id,
          name: tc.function.name,
          arguments: tc.function.arguments
        })) || []
    };

    callbacks?.onComplete?.(result);

    return result;
  }
}

/**
 * Send message to Azure OpenAI API
 */
async function sendToAzureOpenAI(
  messages: ChatMessage[],
  options: SendMessageOptions,
  callbacks?: StreamCallbacks
): Promise<SendMessageResult> {
  if (!options.azureDeployment) {
    throw new Error("Azure OpenAI を使用するにはデプロイ名が必要です");
  }

  // Extract endpoint from apiEndpoint (e.g., https://xxx.openai.azure.com/openai/deployments/yyy/chat/completions?api-version=zzz)
  const url = new URL(options.apiEndpoint);
  const basePath = `${url.protocol}//${url.host}`;
  const apiVersion = url.searchParams.get('api-version') || '2024-02-15-preview';

  const openai = new AzureOpenAI({
    apiKey: options.apiKey,
    endpoint: basePath,
    apiVersion,
    deployment: options.azureDeployment,
    dangerouslyAllowBrowser: true
  });

  if (options.streaming && callbacks?.onContent) {
    // ストリーミング処理
    const stream = await openai.chat.completions.create({
      model: options.model,
      messages: toOpenAIMessages(messages),
      stream: true
    });

    let fullContent = '';
    const toolCallsMap = new Map<string, { id: string; name: string; arguments: string }>();

    for await (const chunk of stream) {
      const delta = chunk.choices[0]?.delta;

      if (delta?.content) {
        fullContent += delta.content;
        callbacks.onContent?.(delta.content);
      }

      if (delta?.tool_calls) {
        for (const tc of delta.tool_calls) {
          const key = tc.index?.toString() || tc.id;
          if (key && !toolCallsMap.has(key)) {
            toolCallsMap.set(key, {
              id: tc.id || '',
              name: tc.function?.name || '',
              arguments: ''
            });
          }
          if (tc.function) {
            const existingToolCall = toolCallsMap.get(key);
            if (existingToolCall) {
              existingToolCall.arguments += (tc.function.arguments || '') as string;
            }
          }
        }
      }
    }

    const toolCalls = Array.from(toolCallsMap.values()).filter(tc => tc.id && tc.name && tc.arguments);

    const result = {
      content: fullContent,
      toolCalls
    };

    callbacks.onComplete?.(result);
    return result;
  } else {
    // 非ストリーミング処理
    const response = await openai.chat.completions.create({
      model: options.model,
      messages: toOpenAIMessages(messages)
    });

    const message = response.choices[0]?.message;
    const result = {
      content: message?.content || "",
      toolCalls: message?.tool_calls
        ?.filter(tc => tc.type === 'function')
        .map(tc => ({
          id: tc.id,
          name: tc.function.name,
          arguments: tc.function.arguments
        })) || []
    };
    
    callbacks?.onComplete?.(result);
    return result;
  }
}

/**
 * Send message to Google Gemini API
 */
async function sendToGemini(
  messages: ChatMessage[],
  options: SendMessageOptions,
  callbacks?: StreamCallbacks
): Promise<SendMessageResult> {
  const genAI = new GoogleGenerativeAI(options.apiKey);
  const model = genAI.getGenerativeModel({ model: options.model });

  // Convert messages to Gemini format
  const systemInstruction = messages.find(m => m.role === 'system')?.content;
  const chatMessages = messages.filter(m => m.role !== 'system');
  
  const chat = model.startChat({
    history: chatMessages.map(msg => ({
      role: msg.role === 'assistant' ? 'model' : 'user',
      parts: [{ text: msg.content }]
    }))
  });

  if (options.streaming && callbacks?.onContent) {
    // ストリーミング処理
    const result = await chat.sendMessageStream(systemInstruction || '');
    let fullContent = '';
    
    for await (const chunk of result.stream) {
      const text = chunk.text();
      fullContent += text;
      callbacks.onContent(text);
    }

    const responseResult = {
      content: fullContent,
      toolCalls: []
    };

    callbacks.onComplete?.(responseResult);
    return responseResult;
  } else {
    // 非ストリーミング処理
    const result = await chat.sendMessage(systemInstruction || '');
    const response = await result.response;

    const responseResult = {
      content: response.text(),
      toolCalls: []
    };
    
    callbacks?.onComplete?.(responseResult);
    return responseResult;
  }
}

/**
 * Send message to Anthropic Claude API
 */
async function sendToAnthropic(
  messages: ChatMessage[],
  options: SendMessageOptions,
  callbacks?: StreamCallbacks
): Promise<SendMessageResult> {
  const anthropic = new Anthropic({
    apiKey: options.apiKey,
    dangerouslyAllowBrowser: true
  });

  const systemMessage = messages.find(m => m.role === 'system');
  const userMessages = toAnthropicMessages(messages);

  if (options.streaming && callbacks?.onContent) {
    // ストリーミング処理
    const stream = await anthropic.messages.stream({
      model: options.model,
      max_tokens: 4096,
      system: systemMessage?.content,
      messages: userMessages
    });

    let fullContent = '';
    const toolCallsMap = new Map<string, { id: string; name: string; arguments: string }>();

    for await (const event of stream) {
      if (event.type === 'content_block_delta') {
        if (event.delta.type === 'text_delta') {
          fullContent += event.delta.text;
          callbacks.onContent(event.delta.text);
        }
      }
      
      // ツール呼び出しの処理
      if (event.type === 'content_block_start' && event.content_block.type === 'tool_use') {
        toolCallsMap.set(event.index.toString(), {
          id: event.content_block.id,
          name: event.content_block.name,
          arguments: ''
        });
      }
      
      if (event.type === 'content_block_delta' && event.delta.type === 'input_json_delta') {
        const toolCall = toolCallsMap.get(event.index.toString());
        if (toolCall) {
          toolCall.arguments += event.delta.partial_json;
        }
      }
    }

    const toolCalls = Array.from(toolCallsMap.values());

    const result = {
      content: fullContent,
      toolCalls
    };

    callbacks.onComplete?.(result);
    return result;
  } else {
    // 非ストリーミング処理
    const response = await anthropic.messages.create({
      model: options.model,
      max_tokens: 4096,
      system: systemMessage?.content,
      messages: userMessages
    });

    const textContent = response.content.find(c => c.type === 'text');
    const toolCalls = response.content
      .filter((c): c is Anthropic.ToolUseBlock => c.type === 'tool_use')
      .map(tc => ({
        id: tc.id,
        name: tc.name,
        arguments: JSON.stringify(tc.input)
      }));

    const result = {
      content: textContent?.text || "",
      toolCalls
    };
    
    callbacks?.onComplete?.(result);
    return result;
  }
}

/**
 * Send message to chat API and get response
 */
export async function sendChatMessage(
  messages: ChatMessage[],
  options: SendMessageOptions,
  callbacks?: StreamCallbacks
): Promise<SendMessageResult> {
  switch (options.apiType) {
    case 'azure':
      return await sendToAzureOpenAI(messages, options, callbacks);
    case 'gemini':
      return await sendToGemini(messages, options, callbacks);
    case 'claude':
      return await sendToAnthropic(messages, options, callbacks);
    case 'chat_completions':
    default:
      return await sendToOpenAI(messages, options, callbacks);
  }
}

/**
 * Execute MCP tool via webview
 */
export async function executeMcpTool(
  name: string,
  args: any,
  onToolCall?: (toolCall: { name: string; args: any }) => void
): Promise<any> {
  return new Promise((resolve, reject) => {
    // Notify tool call start
    if (onToolCall) {
      onToolCall({ name, args });
    }

    // Call MCP tool via webview
    const toolCallId = `tool-${Date.now()}-${Math.random().toString(36).substr(2, 9)}`;
    const message = JSON.stringify({
      method: "tools/call",
      params: {
        name,
        arguments: args,
        toolCallId
      }
    });

    console.log('[executeMcpTool] calling:', name, args);
    webview.postMessage(message);

    // Wait for response via custom event
    const timeout = setTimeout(() => {
      webview.removeEventListener("message", handleResponse);
      reject(new Error(`Tool execution timeout: ${name}`));
    }, 60000); // 60 second timeout

    const handleResponse = (event: any) => {
      try {
        const data = JSON.parse(event.data);
        // Check if this is the response for our tool call
        if (data.toolCallId === toolCallId || (data.method === "toolResult" && data.name === name)) {
          clearTimeout(timeout);
          webview.removeEventListener("message", handleResponse);
          resolve(data.result || data);
        }
      } catch (e) {
        // Ignore parse errors, wait for valid response
      }
    };

    webview.addEventListener("message", handleResponse);
  });
}

// Define webview for browser environment
const webview = (typeof window !== 'undefined' && (window as any).chrome?.webview) || {
  postMessage: (msg: string) => console.log('[webview mock] postMessage:', msg),
  addEventListener: (_event: string, _cb: any) => {},
  removeEventListener: (_event: string, _cb: any) => {}
};

/**
 * Process tool calls and return updated messages
 */
export async function processToolCalls(
  toolCalls: ToolCall[],
  executedToolCallIds: Set<string>,
  onToolExecute: (name: string, args: any, result: any) => void
): Promise<{ executed: ToolCall[]; skipped: number }> {
  const executed: ToolCall[] = [];
  let skipped = 0;

  for (const tc of toolCalls) {
    // Skip already executed tool calls
    if (executedToolCallIds.has(tc.id)) {
      console.log(`Skipping already executed tool call: ${tc.id}`);
      skipped++;
      continue;
    }

    let args = {};
    try {
      args = JSON.parse(tc.arguments);
    } catch (e) {
      console.error("Error parsing tool arguments:", e);
    }

    const result = await executeMcpTool(tc.name, args);
    onToolExecute(tc.name, args, result);

    executedToolCallIds.add(tc.id);
    executed.push(tc);
  }

  return { executed, skipped };
}

/**
 * Build messages array for next API request
 */
export function buildMessagesForNextRequest(
  currentMessages: ChatMessage[],
  assistantContent: string,
  toolCalls: ToolCall[],
  toolResults: Array<{ name: string; content: string; toolCallId: string }>
): ChatMessage[] {
  const newMessages: ChatMessage[] = [...currentMessages];

  // Add assistant message with tool calls
  if (toolCalls.length > 0) {
    newMessages.push({
      role: "assistant" as const,
      content: assistantContent,
      tool_calls: toolCalls.map(tc => ({
        id: tc.id,
        type: "function" as const,
        function: {
          name: tc.name,
          arguments: tc.arguments
        }
      }))
    });
  }

  // Add tool results
  for (const result of toolResults) {
    newMessages.push({
      role: "tool" as const,
      content: result.content,
      name: result.name,
      tool_call_id: result.toolCallId
    });
  }

  return newMessages;
}

export interface McpToolInfo {
  name: string;
  description: string;
  inputSchema: any;
}

/**
 * Fetch available MCP tools from the server
 */
export async function getAvailableTools(): Promise<McpToolInfo[]> {
  try {
    const response = await fetch('/api/mcp/tools');
    if (!response.ok) {
      return [];
    }
    const data = await response.json();
    return data.tools || [];
  } catch (error) {
    console.error('[getAvailableTools] Error fetching tools:', error);
    return [];
  }
}

/**
 * Build system message with tool information for LLM
 */
export function buildSystemMessageWithTools(tools: McpToolInfo[], customSystemMessage?: string): ChatMessage {
  let toolDescription = '';
  
  if (tools.length > 0) {
    toolDescription = '\n\n## 利用可能なツール\n\n' +
      tools.map(tool => {
        const params = tool.inputSchema?.properties ? 
          Object.entries(tool.inputSchema.properties as Record<string, any>)
            .map(([key, value]: [string, any]) => `  - ${key}: ${(value as any).type || 'any'} - ${(value as any).description || ''}`)
            .join('\n')
          : 'パラメータなし';
        
        return `### ${tool.name}\n${tool.description || '説明なし'}\n\nパラメータ:\n${params}`;
      }).join('\n\n');
  } else {
    toolDescription = '\n\n## 利用可能なツール\n\n利用可能なツールはありません。';
  }

  const baseMessage = 'あなたは有能なアシスタントです。ツールを使用してユーザーのタスクを支援してください。';
  
  return {
    role: 'system',
    content: baseMessage + toolDescription + (customSystemMessage ? `\n\n${customSystemMessage}` : '')
  };
}
