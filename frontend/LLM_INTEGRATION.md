# LLM 統合

このドキュメントでは、ひかりチャットアプリにおける LLM（大規模言語モデル）との通信処理について説明します。

## 概要

アプリは複数の LLM プロバイダーをサポートしており、各プロバイダーの公式 SDK を使用して直接 API 通信を行います。

## サポートされている LLM プロバイダー

| API タイプ | プロバイダー | 使用ライブラリ |
|-----------|-------------|---------------|
| `chat_completions` | OpenAI, Grok (xAI), Ollama, Custom | [`openai`](https://www.npmjs.com/package/openai) |
| `azure` | Azure OpenAI | [`openai`](https://www.npmjs.com/package/openai) (AzureOpenAI クラス) |
| `gemini` | Google Gemini | [`@google/generative-ai`](https://www.npmjs.com/package/@google/generative-ai) |
| `claude` | Anthropic Claude | [`@anthropic-ai/sdk`](https://www.npmjs.com/package/@anthropic-ai/sdk) |

## 実装詳細

### ファイル構成

- `frontend/src/chatUtils.ts` - LLM 通信の共通処理

### 分岐ロジック

`apiType` オプションに基づいて、適切な SDK を使用した処理に分岐します。

```typescript
export async function sendChatMessage(
  messages: ChatMessage[],
  options: SendMessageOptions
): Promise<SendMessageResult> {
  switch (options.apiType) {
    case 'azure':
      return await sendToAzureOpenAI(messages, options);
    case 'gemini':
      return await sendToGemini(messages, options);
    case 'claude':
      return await sendToAnthropic(messages, options);
    case 'chat_completions':
    default:
      return await sendToOpenAI(messages, options);
  }
}
```

### 各プロバイダーの実装

#### OpenAI 互換 API (`sendToOpenAI`)

OpenAI、Grok、Ollama、およびカスタムエンドポイントに対応。

```typescript
async function sendToOpenAI(
  messages: ChatMessage[],
  options: SendMessageOptions
): Promise<SendMessageResult> {
  const openai = new OpenAI({
    apiKey: options.apiKey,
    baseURL: options.endpointPreset === 'custom' 
      ? options.apiEndpoint.replace('/chat/completions', '') 
      : undefined
  });

  const response = await openai.chat.completions.create({
    model: options.model,
    messages: toOpenAIMessages(messages)
  });

  // レスポンス処理...
}
```

#### Azure OpenAI (`sendToAzureOpenAI`)

Azure OpenAI Service に対応。エンドポイント URL からベース URL と API バージョンを抽出します。

```typescript
async function sendToAzureOpenAI(
  messages: ChatMessage[],
  options: SendMessageOptions
): Promise<SendMessageResult> {
  const url = new URL(options.apiEndpoint);
  const basePath = `${url.protocol}//${url.host}`;
  const apiVersion = url.searchParams.get('api-version') || '2024-02-15-preview';

  const openai = new AzureOpenAI({
    apiKey: options.apiKey,
    endpoint: basePath,
    apiVersion,
    deployment: options.azureDeployment
  });

  const response = await openai.chat.completions.create({
    model: options.model,
    messages: toOpenAIMessages(messages)
  });

  // レスポンス処理...
}
```

#### Google Gemini (`sendToGemini`)

Google Generative AI SDK を使用。

```typescript
async function sendToGemini(
  messages: ChatMessage[],
  options: SendMessageOptions
): Promise<SendMessageResult> {
  const genAI = new GoogleGenerativeAI(options.apiKey);
  const model = genAI.getGenerativeModel({ model: options.model });

  const chat = model.startChat({
    history: chatMessages.map(msg => ({
      role: msg.role === 'assistant' ? 'model' : 'user',
      parts: [{ text: msg.content }]
    }))
  });

  const result = await chat.sendMessage(systemInstruction || '');
  const response = await result.response;

  return {
    content: response.text(),
    toolCalls: []
  };
}
```

#### Anthropic Claude (`sendToAnthropic`)

Anthropic SDK を使用。system メッセージとツール呼び出しをサポート。

```typescript
async function sendToAnthropic(
  messages: ChatMessage[],
  options: SendMessageOptions
): Promise<SendMessageResult> {
  const anthropic = new Anthropic({
    apiKey: options.apiKey
  });

  const systemMessage = messages.find(m => m.role === 'system');
  const userMessages = toAnthropicMessages(messages);

  const response = await anthropic.messages.create({
    model: options.model,
    max_tokens: 4096,
    system: systemMessage?.content,
    messages: userMessages
  });

  // テキストとツール呼び出しを処理...
}
```

## メッセージ形式の変換

各 LLM プロバイダーは異なるメッセージ形式を持つため、内部形式から各プロバイダーの形式に変換します。

### 内部形式

```typescript
interface ChatMessage {
  role: 'user' | 'assistant' | 'system' | 'tool' | 'error';
  content: string;
  name?: string;
  tool_call_id?: string;
}
```

### OpenAI 形式への変換

```typescript
function toOpenAIMessages(messages: ChatMessage[]): OpenAI.Chat.ChatCompletionMessageParam[] {
  return messages.map(msg => ({
    role: msg.role === 'error' ? 'user' : msg.role,
    content: msg.content,
    ...(msg.name && { name: msg.name }),
    ...(msg.tool_call_id && { tool_call_id: msg.tool_call_id })
  }));
}
```

### Anthropic 形式への変換

```typescript
function toAnthropicMessages(messages: ChatMessage[]): Anthropic.MessageParam[] {
  return messages
    .filter(msg => msg.role !== 'system')
    .map(msg => ({
      role: msg.role === 'assistant' ? 'assistant' : 'user',
      content: msg.content
    }));
}
```

## ツール呼び出しの処理

LLM からのツール呼び出しレスポンスは、共通形式に変換されます。

```typescript
interface ToolCall {
  id: string;
  name: string;
  arguments: string;
}
```

### OpenAI 形式からの抽出

```typescript
toolCalls: message?.tool_calls
  ?.filter(tc => tc.type === 'function')
  .map(tc => ({
    id: tc.id,
    name: tc.function.name,
    arguments: tc.function.arguments
  })) || []
```

### Anthropic 形式からの抽出

```typescript
const toolCalls = response.content
  .filter((c): c is Anthropic.ToolUseBlock => c.type === 'tool_use')
  .map(tc => ({
    id: tc.id,
    name: tc.name,
    arguments: JSON.stringify(tc.input)
  }));
```

## 依存関係

```json
{
  "dependencies": {
    "openai": "^x.x.x",
    "@anthropic-ai/sdk": "^x.x.x",
    "@google/generative-ai": "^x.x.x"
  }
}
```

## 設定

設定画面で以下の項目を設定できます：

- **API タイプ**: `chat_completions`, `azure`, `gemini`, `claude`
- **エンドポイントプリセット**: `openai`, `azure_openai`, `gemini`, `grok`, `anthropic`, `ollama`, `custom`
- **API エンドポイント**: LLM の API URL
- **API キー**: 認証トークン
- **モデル**: 使用するモデル名
- **Azure デプロイ名**: Azure OpenAI 使用時のデプロイ名
