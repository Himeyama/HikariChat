import OpenAI from 'openai';
import Anthropic from '@anthropic-ai/sdk';
import { GoogleGenerativeAI } from '@google/generative-ai';
import { AzureOpenAI } from 'openai';

export interface ChatMessage {
  role: 'user' | 'assistant' | 'system' | 'tool' | 'error';
  content: string;
  name?: string;
  tool_call_id?: string;
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
 * Execute MCP tool
 */
export async function executeMcpTool(name: string, args: any): Promise<any> {
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
}

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
  _assistantContent: string,
  _toolCalls: ToolCall[],
  toolResults: Array<{ name: string; content: string; toolCallId: string }>
): ChatMessage[] {
  const newMessages: ChatMessage[] = [...currentMessages];

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
