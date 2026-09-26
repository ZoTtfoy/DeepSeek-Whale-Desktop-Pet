using System;
using System.Collections;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace DeepSeekWhaleStandalone
{
    // Text-only messages. The caller owns the conversation history and appends
    // successful assistant replies after SendAsync returns.
    public sealed class ChatTurn
    {
        public string Role { get; set; }
        public string Content { get; set; }

        public ChatTurn() { }

        public ChatTurn(string role, string content)
        {
            Role = role;
            Content = content;
        }
    }

    public sealed class ChatQueryException : Exception
    {
        public ChatQueryException(string message) : base(message) { }
    }

    public sealed class ChatClient
    {
        public const string DefaultModel = "deepseek-flash";
        public const string ProModel = "deepseek-v4-pro";
        private static readonly Uri Endpoint = new Uri("https://api.deepseek.com/chat/completions");
        private readonly HttpMessageHandler handler;

        public ChatClient() { }

        // The injectable handler is intended for local tests; it remains caller-owned.
        public ChatClient(HttpMessageHandler handler)
        {
            if (handler == null) throw new ArgumentNullException("handler");
            this.handler = handler;
        }

        // history must include the current user message. It may contain a system
        // message followed by recent user/assistant turns, in chronological order.
        public async Task<string> SendAsync(string apiKey, IList<ChatTurn> history,
            string model = DefaultModel, CancellationToken cancellationToken = default(CancellationToken))
        {
            if (String.IsNullOrWhiteSpace(apiKey))
                throw new ChatQueryException("请先在设置中填写 DeepSeek API Key。");
            if (model != DefaultModel && model != ProModel)
                throw new ChatQueryException("请选择受支持的 DeepSeek 模型。");
            if (history == null || history.Count == 0)
                throw new ChatQueryException("请输入要发送的消息。");

            List<Dictionary<string, string>> messages = new List<Dictionary<string, string>>();
            foreach (ChatTurn turn in history)
            {
                if (turn == null || (turn.Role != "system" && turn.Role != "user" && turn.Role != "assistant") ||
                    String.IsNullOrWhiteSpace(turn.Content))
                    throw new ChatQueryException("聊天记录包含无效消息。");
                Dictionary<string, string> item = new Dictionary<string, string>();
                item["role"] = turn.Role;
                item["content"] = turn.Content;
                messages.Add(item);
            }
            if (history[history.Count - 1].Role != "user")
                throw new ChatQueryException("最后一条聊天消息必须来自用户。");

            Dictionary<string, object> body = new Dictionary<string, object>();
            body["model"] = model;
            body["messages"] = messages;
            body["thinking"] = new Dictionary<string, string> { { "type", "disabled" } };
            body["max_tokens"] = 2048;
            body["stream"] = false;
            string json = new JavaScriptSerializer().Serialize(body);

            using (HttpClient client = handler == null
                ? new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
                : new HttpClient(handler, false))
            using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, Endpoint))
            {
                client.Timeout = TimeSpan.FromSeconds(90);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey.Trim());
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                request.Content = new StringContent(json, Encoding.UTF8, "application/json");

                HttpResponseMessage response;
                try
                {
                    response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    if (cancellationToken.IsCancellationRequested) throw;
                    throw new ChatQueryException("对话等待超时，请稍后重试。");
                }
                catch (HttpRequestException)
                {
                    throw new ChatQueryException("无法连接 DeepSeek，请检查网络连接后重试。");
                }

                using (response)
                {
                    switch ((int)response.StatusCode)
                    {
                        case 400:
                        case 422:
                            throw new ChatQueryException("DeepSeek 拒绝了本次请求，请缩短对话后重试。");
                        case 401:
                        case 403:
                            throw new ChatQueryException("API Key 无效或无权对话，请检查密钥。");
                        case 402:
                            throw new ChatQueryException("DeepSeek 账户余额不足，请先充值。");
                        case 429:
                            throw new ChatQueryException("对话请求过于频繁，请稍后重试。");
                        case 500:
                        case 503:
                            throw new ChatQueryException("DeepSeek 服务暂时繁忙，请稍后重试。");
                    }
                    if (!response.IsSuccessStatusCode)
                        throw new ChatQueryException("DeepSeek 返回错误（HTTP " +
                            (int)response.StatusCode + "），请稍后重试。");

                    string responseJson;
                    try { responseJson = await response.Content.ReadAsStringAsync().ConfigureAwait(false); }
                    catch (Exception) { throw new ChatQueryException("读取 DeepSeek 回复失败，请重试。"); }
                    return ParseAssistantText(responseJson);
                }
            }
        }

        public static string ParseAssistantText(string json)
        {
            object parsed;
            try { parsed = new JavaScriptSerializer().DeserializeObject(json); }
            catch (Exception) { throw new ChatQueryException("DeepSeek 回复格式无法识别。"); }

            Dictionary<string, object> root = parsed as Dictionary<string, object>;
            if (root == null) throw new ChatQueryException("DeepSeek 回复格式无法识别。");
            object choicesObject;
            if (!root.TryGetValue("choices", out choicesObject))
                throw new ChatQueryException("DeepSeek 回复缺少内容。");
            IList choices = choicesObject as IList;
            if (choices == null || choices.Count == 0)
                throw new ChatQueryException("DeepSeek 回复缺少内容。");
            Dictionary<string, object> choice = choices[0] as Dictionary<string, object>;
            if (choice == null) throw new ChatQueryException("DeepSeek 回复格式无法识别。");
            object finishReason;
            if (choice.TryGetValue("finish_reason", out finishReason) && finishReason is string &&
                (string)finishReason != "stop")
            {
                if ((string)finishReason == "length")
                    throw new ChatQueryException("回答超过长度上限，请缩短问题后重试。");
                throw new ChatQueryException("DeepSeek 未能完成回答，请重试。");
            }
            object messageObject;
            if (!choice.TryGetValue("message", out messageObject))
                throw new ChatQueryException("DeepSeek 回复缺少内容。");
            Dictionary<string, object> message = messageObject as Dictionary<string, object>;
            if (message == null) throw new ChatQueryException("DeepSeek 回复格式无法识别。");
            object content;
            if (!message.TryGetValue("content", out content) || !(content is string) ||
                String.IsNullOrWhiteSpace((string)content))
                throw new ChatQueryException("DeepSeek 返回了空回复，请重试。");
            return (string)content;
        }
    }
}
