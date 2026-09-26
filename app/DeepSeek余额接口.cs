using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace DeepSeekBalanceViewer
{
    public sealed class BalanceInfo
    {
        public string Currency { get; set; }
        public string Total { get; set; }
        public string Granted { get; set; }
        public string ToppedUp { get; set; }
    }

    public sealed class BalanceResult
    {
        public bool IsAvailable { get; set; }
        public List<BalanceInfo> Items { get; set; }
    }

    public sealed class BalanceQueryException : Exception
    {
        public BalanceQueryException(string message) : base(message) { }
    }

    public sealed class BalanceClient
    {
        private readonly HttpMessageHandler handler;
        private static readonly Uri Endpoint = new Uri("https://api.deepseek.com/user/balance");

        public BalanceClient() { }
        public BalanceClient(HttpMessageHandler handler) { this.handler = handler; }

        public async Task<BalanceResult> QueryAsync(string apiKey)
        {
            if (String.IsNullOrWhiteSpace(apiKey))
                throw new BalanceQueryException("请先输入 DeepSeek API Key。");

            using (HttpClient client = handler == null
                ? new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
                : new HttpClient(handler, false))
            using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, Endpoint))
            {
                client.Timeout = TimeSpan.FromSeconds(15);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey.Trim());
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

                HttpResponseMessage response;
                try
                {
                    response = await client.SendAsync(request).ConfigureAwait(false);
                }
                catch (TaskCanceledException)
                {
                    throw new BalanceQueryException("查询超时。请检查网络连接后重试。");
                }
                catch (HttpRequestException)
                {
                    throw new BalanceQueryException("无法连接 DeepSeek。请检查网络连接后重试。");
                }

                using (response)
                {
                    if (response.StatusCode == HttpStatusCode.Unauthorized ||
                        response.StatusCode == HttpStatusCode.Forbidden)
                        throw new BalanceQueryException("API Key 无效或无权查询余额。请检查密钥。");
                    if ((int)response.StatusCode == 429)
                        throw new BalanceQueryException("请求过于频繁，请稍后重试。");
                    if (!response.IsSuccessStatusCode)
                        throw new BalanceQueryException("DeepSeek 返回错误（HTTP " +
                            (int)response.StatusCode + "）。请稍后重试。");

                    string json;
                    try
                    {
                        json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                        return ParseResponse(json);
                    }
                    catch (BalanceQueryException) { throw; }
                    catch (Exception)
                    {
                        throw new BalanceQueryException("返回的余额数据无法识别，请稍后重试。");
                    }
                }
            }
        }

        public static BalanceResult ParseResponse(string json)
        {
            object parsed;
            try { parsed = new JavaScriptSerializer().DeserializeObject(json); }
            catch (Exception) { throw new BalanceQueryException("返回的余额数据无法识别，请稍后重试。"); }

            Dictionary<string, object> root = parsed as Dictionary<string, object>;
            if (root == null || !root.ContainsKey("is_available") ||
                !(root["is_available"] is bool) || !root.ContainsKey("balance_infos"))
                throw new BalanceQueryException("返回的余额数据缺少必要字段。");

            IEnumerable entries = root["balance_infos"] as IEnumerable;
            if (entries == null || root["balance_infos"] is string)
                throw new BalanceQueryException("返回的余额数据缺少必要字段。");

            BalanceResult result = new BalanceResult();
            result.IsAvailable = (bool)root["is_available"];
            result.Items = new List<BalanceInfo>();
            foreach (object entry in entries)
            {
                Dictionary<string, object> item = entry as Dictionary<string, object>;
                if (item == null)
                    throw new BalanceQueryException("返回的余额数据无法识别，请稍后重试。");
                result.Items.Add(new BalanceInfo {
                    Currency = RequiredText(item, "currency"),
                    Total = RequiredText(item, "total_balance"),
                    Granted = RequiredText(item, "granted_balance"),
                    ToppedUp = RequiredText(item, "topped_up_balance")
                });
            }
            return result;
        }

        private static string RequiredText(Dictionary<string, object> item, string field)
        {
            object value;
            if (!item.TryGetValue(field, out value) || !(value is string))
                throw new BalanceQueryException("返回的余额数据缺少必要字段。");
            return (string)value;
        }
    }

}

