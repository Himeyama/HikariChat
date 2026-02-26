import OpenAI from 'openai';
import Anthropic from '@anthropic-ai/sdk';
import { GoogleGenAI } from '@google/genai';
import { AzureOpenAI } from 'openai';

// ============================================================
// Types
// ============================================================

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

/**
 * MCP Tool information compatible with @modelcontextprotocol/sdk types
 */
export interface McpToolInfo {
  jsonSchema: any;
  name: string;
  description?: string;
  inputSchema?: Record<string, unknown>;
  inputSchemaJson?: string;
}

export interface SendMessageOptions {
  apiKey: string;
  apiEndpoint: string;
  model: string;
  apiType: 'azure' | 'gemini' | 'claude' | 'chat_completions';
  endpointPreset: string;
  azureDeployment: string;
  streaming: boolean;
  mcpEnabled: boolean;
  tools?: any[];
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

// ============================================================
// Message converters
// ============================================================

/**
 * Convert internal ChatMessage[] to OpenAI format.
 * 'error' role is treated as 'user' since OpenAI does not support it.
 */
function toOpenAIMessages(messages: ChatMessage[]): OpenAI.Chat.ChatCompletionMessageParam[] {
  return messages.map((msg): OpenAI.Chat.ChatCompletionMessageParam => {
    const base = {
      content: msg.content,
      ...(msg.name ? { name: msg.name } : {}),
    };

    if (msg.role === 'tool') {
      if (!msg.tool_call_id) {
        console.warn('[toOpenAIMessages] tool message missing tool_call_id, falling back to user');
        return { role: 'user', content: msg.content };
      }
      return { role: 'tool', tool_call_id: msg.tool_call_id, content: msg.content };
    }

    if (msg.role === 'assistant' && msg.tool_calls) {
      return {
        role: 'assistant',
        content: msg.content,
        tool_calls: msg.tool_calls,
      };
    }

    const role = msg.role === 'error' ? 'user' : msg.role;
    return { role, ...base } as OpenAI.Chat.ChatCompletionMessageParam;
  });
}

/**
 * Convert internal ChatMessage[] to Anthropic format.
 * - System messages are excluded (handled via the `system` parameter).
 * - tool role messages are converted to user messages with tool_result content.
 * - error role is treated as user.
 * - Consecutive same-role messages are merged (Anthropic requires alternating).
 */
function toAnthropicMessages(messages: ChatMessage[]): Anthropic.MessageParam[] {
  const filtered = messages.filter(m => m.role !== 'system');

  const converted: Anthropic.MessageParam[] = filtered.map((msg): Anthropic.MessageParam => {
    if (msg.role === 'tool') {
      // Wrap tool result in the correct Anthropic content block format
      return {
        role: 'user',
        content: [
          {
            type: 'tool_result',
            tool_use_id: msg.tool_call_id ?? '',
            content: msg.content,
          },
        ],
      };
    }

    if (msg.role === 'assistant' && msg.tool_calls) {
      // Include tool_use blocks so Anthropic can correlate tool results
      const contentBlocks: Anthropic.ContentBlockParam[] = [];
      if (msg.content) {
        contentBlocks.push({ type: 'text', text: msg.content });
      }
      for (const tc of msg.tool_calls) {
        let input: Record<string, unknown> = {};
        try {
          input = JSON.parse(tc.function.arguments);
        } catch {
          console.error('[toAnthropicMessages] Failed to parse tool arguments:', tc.function.arguments);
        }
        contentBlocks.push({
          type: 'tool_use',
          id: tc.id,
          name: tc.function.name,
          input,
        });
      }
      return { role: 'assistant', content: contentBlocks };
    }

    const role = msg.role === 'error' ? 'user' : (msg.role as 'user' | 'assistant');
    return { role, content: msg.content };
  });

  // Anthropic requires strictly alternating user/assistant messages.
  // Merge consecutive messages with the same role.
  return mergeConsecutiveSameRole(converted);
}

/**
 * Merge consecutive Anthropic messages that share the same role.
 * This prevents API errors when multiple tool results or user messages appear back-to-back.
 */
function mergeConsecutiveSameRole(messages: Anthropic.MessageParam[]): Anthropic.MessageParam[] {
  if (messages.length === 0) return [];

  const merged: Anthropic.MessageParam[] = [messages[0]];

  for (let i = 1; i < messages.length; i++) {
    const prev = merged[merged.length - 1];
    const curr = messages[i];

    if (prev.role === curr.role) {
      // Normalize both to array form and concat
      const prevBlocks = normalizeToBlocks(prev.content);
      const currBlocks = normalizeToBlocks(curr.content);
      prev.content = [...prevBlocks, ...currBlocks];
    } else {
      merged.push(curr);
    }
  }

  return merged;
}

function normalizeToBlocks(
  content: string | Anthropic.ContentBlockParam[]
): Anthropic.ContentBlockParam[] {
  if (typeof content === 'string') {
    return [{ type: 'text', text: content }];
  }
  return content;
}

// ============================================================
// Streaming helpers
// ============================================================

/**
 * Accumulate OpenAI streaming tool_calls deltas into a map keyed by numeric index.
 * Using the numeric index (not id) is the correct approach because the id is only
 * present in the first delta for a given tool call.
 */
function accumulateOpenAIToolCalls(
  map: Map<number, { id: string; name: string; arguments: string }>,
  deltas: OpenAI.Chat.ChatCompletionChunk.Choice.Delta.ToolCall[]
): void {
  for (const tc of deltas) {
    // index is the stable key across delta chunks
    const idx = tc.index ?? 0;
    if (!map.has(idx)) {
      map.set(idx, { id: tc.id ?? '', name: tc.function?.name ?? '', arguments: '' });
    }
    const entry = map.get(idx)!;
    if (tc.id && !entry.id) entry.id = tc.id;
    if (tc.function?.name && !entry.name) entry.name = tc.function.name;
    if (tc.function?.arguments) entry.arguments += tc.function.arguments;
  }
}

function toolCallMapToArray(
  map: Map<number, { id: string; name: string; arguments: string }>
): ToolCall[] {
  return Array.from(map.values()).filter(tc => tc.id && tc.name);
}

// ============================================================
// Provider implementations
// ============================================================

async function sendToOpenAI(
  messages: ChatMessage[],
  options: SendMessageOptions,
  callbacks?: StreamCallbacks
): Promise<SendMessageResult> {
  const openai = new OpenAI({
    apiKey: options.apiKey,
    baseURL: options.apiEndpoint.replace('/chat/completions', ''),
    dangerouslyAllowBrowser: true,
  });

  const baseBody = {
    model: options.model,
    messages: toOpenAIMessages(messages),
    ...(options.tools && options.tools.length > 0 ? { tools: options.tools } : {}),
  };

  if (options.streaming && callbacks?.onContent) {
    const stream = await openai.chat.completions.create({ ...baseBody, stream: true });
    let fullContent = '';
    const toolCallMap = new Map<number, { id: string; name: string; arguments: string }>();

    for await (const chunk of stream) {
      const delta = chunk.choices[0]?.delta;
      if (delta?.content) {
        fullContent += delta.content;
        callbacks.onContent(delta.content);
      }
      if (delta?.tool_calls) {
        accumulateOpenAIToolCalls(toolCallMap, delta.tool_calls);
      }
    }

    const result: SendMessageResult = { content: fullContent, toolCalls: toolCallMapToArray(toolCallMap) };
    callbacks.onComplete?.(result);
    return result;
  }

  const response = await openai.chat.completions.create({ ...baseBody, stream: false });
  const message = response.choices[0]?.message;
  const result: SendMessageResult = {
    content: message?.content ?? '',
    toolCalls:
      message?.tool_calls
        ?.filter(tc => tc.type === 'function')
        .map(tc => ({ id: tc.id, name: tc.function.name, arguments: tc.function.arguments ?? '{}' })) ?? [],
  };
  callbacks?.onComplete?.(result);
  return result;
}

async function sendToAzureOpenAI(
  messages: ChatMessage[],
  options: SendMessageOptions,
  callbacks?: StreamCallbacks
): Promise<SendMessageResult> {
  if (!options.azureDeployment && options.endpointPreset == "")
    throw new Error('Azure OpenAI を使用するにはデプロイ名が必要です');

  const url = new URL(options.apiEndpoint);
  let endpoint = `${url.protocol}//${url.host}`;

  if(options.endpointPreset != "azure_openai")
    endpoint = options.apiEndpoint;

  const apiVersion = url.searchParams.get('api-version') ?? '2024-02-15-preview';

  const openai = new AzureOpenAI({
    apiKey: options.apiKey,
    endpoint,
    apiVersion,
    deployment: options.azureDeployment,
    dangerouslyAllowBrowser: true,
  });

  const baseBody = {
    model: options.model,
    messages: toOpenAIMessages(messages),
    ...(options.tools && options.tools.length > 0 ? { tools: options.tools } : {}),
  };

  if (options.streaming && callbacks?.onContent) {
    const stream = await openai.chat.completions.create({ ...baseBody, stream: true });
    let fullContent = '';
    const toolCallMap = new Map<number, { id: string; name: string; arguments: string }>();

    for await (const chunk of stream) {
      const delta = chunk.choices[0]?.delta;
      if (delta?.content) {
        fullContent += delta.content;
        callbacks.onContent(delta.content);
      }
      if (delta?.tool_calls) {
        accumulateOpenAIToolCalls(toolCallMap, delta.tool_calls);
      }
    }

    const result: SendMessageResult = { content: fullContent, toolCalls: toolCallMapToArray(toolCallMap) };
    callbacks.onComplete?.(result);
    return result;
  }

  const response = await openai.chat.completions.create({ ...baseBody, stream: false });
  const message = response.choices[0]?.message;
  const result: SendMessageResult = {
    content: message?.content ?? '',
    toolCalls:
      message?.tool_calls
        ?.filter(tc => tc.type === 'function')
        .map(tc => ({ id: tc.id, name: tc.function.name, arguments: tc.function.arguments ?? '{}' })) ?? [],
  };
  callbacks?.onComplete?.(result);
  return result;
}

async function sendToGemini(
  messages: ChatMessage[],
  options: SendMessageOptions,
  callbacks?: StreamCallbacks
): Promise<SendMessageResult> {
  const ai = new GoogleGenAI({
    apiKey: options.apiKey,
    ...(options.endpointPreset === 'custom' && options.apiEndpoint
      ? { httpOptions: { baseUrl: options.apiEndpoint } }
      : {}),
  });

  const systemInstruction = messages.find(m => m.role === 'system')?.content;
  const nonSystemMessages = messages.filter(m => m.role !== 'system');

  const contents = nonSystemMessages.map(msg => ({
    role: msg.role === 'assistant' ? 'model' : 'user',
    parts: [{ text: msg.content }],
  }));

  const config = systemInstruction ? { systemInstruction } : undefined;

  if (options.streaming && callbacks?.onContent) {
    const stream = await ai.models.generateContentStream({
      model: options.model,
      contents,
      ...(config ? { config } : {}),
    });
    let fullContent = '';

    for await (const chunk of stream) {
      const text = chunk.text;
      if (text) {
        fullContent += text;
        callbacks.onContent(text);
      }
    }

    const result: SendMessageResult = { content: fullContent, toolCalls: [] };
    callbacks.onComplete?.(result);
    return result;
  }

  const response = await ai.models.generateContent({
    model: options.model,
    contents,
    ...(config ? { config } : {}),
  });
  const result: SendMessageResult = { content: response.text ?? '', toolCalls: [] };
  callbacks?.onComplete?.(result);
  return result;
}

async function sendToAnthropic(
  messages: ChatMessage[],
  options: SendMessageOptions,
  callbacks?: StreamCallbacks
): Promise<SendMessageResult> {
  const anthropic = new Anthropic({ apiKey: options.apiKey, dangerouslyAllowBrowser: true });

  const systemMessage = messages.find(m => m.role === 'system')?.content;
  const anthropicMessages = toAnthropicMessages(messages);

  const baseParams: Omit<Anthropic.MessageCreateParamsNonStreaming, 'stream'> = {
    model: options.model,
    max_tokens: 4096,
    messages: anthropicMessages,
    ...(systemMessage ? { system: systemMessage } : {}),
    ...(options.tools && options.tools.length > 0 ? { tools: options.tools } : {}),
  };

  if (options.streaming && callbacks?.onContent) {
    const stream = anthropic.messages.stream({ ...baseParams });
    let fullContent = '';
    const toolCallMap = new Map<string, { id: string; name: string; arguments: string }>();

    for await (const event of stream) {
      if (event.type === 'content_block_start' && event.content_block.type === 'tool_use') {
        toolCallMap.set(String(event.index), {
          id: event.content_block.id,
          name: event.content_block.name,
          arguments: '',
        });
      }

      if (event.type === 'content_block_delta') {
        if (event.delta.type === 'text_delta') {
          fullContent += event.delta.text;
          callbacks.onContent(event.delta.text);
        } else if (event.delta.type === 'input_json_delta') {
          const entry = toolCallMap.get(String(event.index));
          if (entry) entry.arguments += event.delta.partial_json;
        }
      }
    }

    const result: SendMessageResult = {
      content: fullContent,
      toolCalls: Array.from(toolCallMap.values()),
    };
    callbacks.onComplete?.(result);
    return result;
  }

  const response = await anthropic.messages.create({ ...baseParams, stream: false });

  const textBlock = response.content.find((c): c is Anthropic.TextBlock => c.type === 'text');
  const toolCalls: ToolCall[] = response.content
    .filter((c): c is Anthropic.ToolUseBlock => c.type === 'tool_use')
    .map(tc => ({ id: tc.id, name: tc.name, arguments: JSON.stringify(tc.input) }));

  const result: SendMessageResult = { content: textBlock?.text ?? '', toolCalls };
  callbacks?.onComplete?.(result);
  return result;
}

// ============================================================
// Public API
// ============================================================

export async function sendChatMessage(
  messages: ChatMessage[],
  options: SendMessageOptions,
  callbacks?: StreamCallbacks
): Promise<SendMessageResult> {
  try {
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
  } catch (error) {
    const err = error instanceof Error ? error : new Error(String(error));
    callbacks?.onError?.(err);
    throw err;
  }
}

// ============================================================
// MCP / Tool utilities
// ============================================================

// Webview shim for non-browser environments
const webview =
  typeof window !== 'undefined' && (window as any).chrome?.webview
    ? (window as any).chrome.webview
    : {
        postMessage: (msg: string) => console.log('[webview mock] postMessage:', msg),
        addEventListener: (_event: string, _cb: any) => {},
        removeEventListener: (_event: string, _cb: any) => {},
      };

/**
 * Execute MCP tool via C# backend HTTP API
 */
export async function executeMcpTool(
  name: string,
  args: unknown,
  onToolCall?: (toolCall: { name: string; args: unknown }) => void
): Promise<unknown> {
  onToolCall?.({ name, args });

  try {
    // Try HTTP API first (C# backend)
    const response = await fetch('http://localhost:30078/api/mcp/execute', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ name, arguments: args }),
    });

    if (response.ok) {
      const data = await response.json();
      return data.result ?? data;
    }

    // Fallback to webview postMessage for backward compatibility
    const toolCallId = `tool-${Date.now()}-${Math.random().toString(36).substring(2, 11)}`;
    const message = JSON.stringify({
      method: 'tools/call',
      params: { name, arguments: args, toolCallId },
    });

    return new Promise((resolve, reject) => {
      const timeout = setTimeout(() => {
        webview.removeEventListener('message', handleResponse);
        reject(new Error(`Tool execution timeout: ${name}`));
      }, 60_000);

      const handleResponse = (event: MessageEvent) => {
        let data: any;
        try {
          data = JSON.parse(event.data);
        } catch {
          return;
        }

        if (data.toolCallId === toolCallId || (data.method === 'toolResult' && data.name === name)) {
          clearTimeout(timeout);
          webview.removeEventListener('message', handleResponse);
          resolve(data.result ?? data);
        }
      };

      webview.addEventListener('message', handleResponse);
      webview.postMessage(message);
    });
  } catch (error) {
    console.error('[executeMcpTool] Error:', error);
    throw error;
  }
}

/**
 * Build the messages array to send after tool results are available.
 * NOTE: For Anthropic (Claude), tool results must be in user messages with
 * tool_result content blocks — this is handled by toAnthropicMessages().
 */
export function buildMessagesForNextRequest(
  currentMessages: ChatMessage[],
  assistantContent: string,
  toolCalls: ToolCall[],
  toolResults: Array<{ name: string; content: string; toolCallId: string }>
): ChatMessage[] {
  const next: ChatMessage[] = [...currentMessages];

  if (toolCalls.length > 0) {
    next.push({
      role: 'assistant',
      content: assistantContent,
      tool_calls: toolCalls.map(tc => ({
        id: tc.id,
        type: 'function' as const,
        function: { name: tc.name, arguments: tc.arguments },
      })),
    });
  }

  for (const r of toolResults) {
    next.push({
      role: 'tool',
      content: r.content,
      name: r.name,
      tool_call_id: r.toolCallId,
    });
  }

  return next;
}

/**
 * Get available MCP tools from C# backend
 * Uses MCP SDK types for better type safety
 */
export async function getAvailableTools(): Promise<McpToolInfo[]> {
  try {
    const response = await fetch('http://localhost:30078/api/mcp/tools');
    if (!response.ok) return [];
    const data = await response.json();
    console.log(data)
    return (data.tools as McpToolInfo[]) ?? [];
  } catch (error) {
    console.error('[getAvailableTools] Error fetching tools:', error);
    return [];
  }
}

export function convertToOpenAITools(tools: McpToolInfo[]): OpenAI.Chat.ChatCompletionTool[] {
  return tools.map(tool => {
    const schema = tool.jsonSchema; // returnJsonSchema → jsonSchema に変更

    const parameters = schema
      ? {
          type: schema.type ?? 'object',
          properties: schema.properties ?? {},
          required: schema.required ?? [],
        }
      : { type: 'object', properties: {}, required: [] };

    return {
      type: 'function' as const,
      function: {
        name: tool.name,
        description: tool.description ?? '',
        parameters,
      },
    };
  });
}
/**
 * Convert MCP tools to Anthropic format.
 * Anthropic requires { type: 'custom', name, description, input_schema } instead of OpenAI's function wrapper.
 */
export function convertToAnthropicTools(tools: McpToolInfo[]): Anthropic.Tool[] {
  return tools.map(tool => {
    const schema = tool.jsonSchema;
    const input_schema: Anthropic.Tool.InputSchema = {
      type: 'object',
      properties: schema?.properties ?? {},
      ...(schema?.required ? { required: schema.required } : {}),
    };
    return {
      type: 'custom' as const,
      name: tool.name,
      description: tool.description ?? '',
      input_schema,
    };
  });
}