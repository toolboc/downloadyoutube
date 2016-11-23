using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Net.Http;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using System.Net;
using System.Diagnostics;

using Xamarin.Forms;

namespace downloadyoutube
{
    public class Service 
    {
        public async Task<Dictionary<string, string>> GetFormats(string youtubeURL)
        {
            #region // start
            var page = await DownloadStringAsync(youtubeURL);
            var playerScript = FindMatch(page, "<script>(var ytplayer.*)</script>");

            var videoId = FindMatch(playerScript, @"\""video_id\"":\s*\""([^\""]+)\""");
            var videoFormats = FindMatch(playerScript, @"\""url_encoded_fmt_stream_map\"":\s*\""([^\""]+)\""");
            var videoAdaptFormats = FindMatch(playerScript, @"\""adaptive_fmts\"":\s*\""([^\""]+)\""");

            var scriptURL = FindMatch(playerScript, @"\""js\"":\s*\""([^\""]+)\""");
            scriptURL = scriptURL.Replace("\\", "");
            if (scriptURL.IndexOf("/") == 0)
            {
                var protocol = FindMatch(youtubeURL, "(https*:)");
                scriptURL = protocol + scriptURL;
            }

            var decodeArray = await FetchSignatureScript(scriptURL);

            var videoTitle = FindMatch(page, "<title>(.*)</title>");
            videoTitle = videoTitle.Remove(videoTitle.LastIndexOf(" - YouTube"));
            #endregion

            #region  // parse the formats map
            videoFormats = videoFormats.Replace("\\u0026", "&");

            char sep1 = ',';
            char sep2 = '&';
            char sep3 = '=';

            var videoURL = new Dictionary<string, string>();
            var videoSignature = new Dictionary<string, string>();

            if (videoAdaptFormats != null)
                videoFormats = videoFormats + sep1 + videoAdaptFormats;

            var videoFormatsGroup = videoFormats.Split(sep1);

            for (var i = 0; i < videoFormatsGroup.Length; i++)
            {
                var videoFormatsElem = videoFormatsGroup[i].Split(sep2);
                var videoFormatsPair = new Dictionary<string, string>();
                for (var j = 0; j < videoFormatsElem.Length; j++)
                {
                    var pair = videoFormatsElem[j].Split(sep3);

                    if (pair.Length == 2)
                    {
                        videoFormatsPair[pair[0]] = pair[1];
                    }
                }

                if (!videoFormatsPair.ContainsKey("url")) 
                    continue;

                var url = WebUtility.UrlDecode(WebUtility.UrlDecode(videoFormatsPair["url"])).Replace(@"\\\/", "/").Replace("\\u0026", "&");

                if (!videoFormatsPair.ContainsKey("itag"))
                    continue;

                var itag = videoFormatsPair["itag"];


                if (videoFormatsPair.ContainsKey("sig"))
                {
                    url = url + "&signature=" + videoFormatsPair["sig"];
                    videoSignature[itag] = null;
                }
                else if (videoFormatsPair.ContainsKey("signature"))
                {
                    url = url + "&signature=" + videoFormatsPair["signature"];
                    videoSignature[itag] = null;
                }
                else if (videoFormatsPair.ContainsKey("s"))
                {
                    url = url + "&signature=" + DecryptSignature(videoFormatsPair["s"], decodeArray);
                    videoSignature[itag] = videoFormatsPair["s"];
                }

                if (url.ToLower().IndexOf("ratebypass") == -1)
                { // speed up download for dash
                    url = url + "&ratebypass=yes";
                }

                if (url.ToLower().IndexOf("http") == 0)
                { // validate URL

                    videoURL[itag] = url; //+ "&title=" + videoTitle;
                }
            }
            #endregion

            return videoURL;
        }

        private async Task<List<int>> FetchSignatureScript(string scriptURL)
        {
            var sourceCode = await DownloadStringAsync(scriptURL);
            return FindSignatureCode(sourceCode);
        }

        private List<int> FindSignatureCode(string sourceCode)
        {
            var signatureFunctionName = FindMatch(sourceCode, @"\.set\s*\(""signature""\s*,\s*([a-zA-Z0-9_$][\w$]*)\(");

            //Optimization of Gantt's technique - functionName not needed
            var regExp = @"\s*\([\w$]*\)\s*{[\w$]*=[\w$]*\.split\(""""\);\n*(.+);return [\w$]*\.join";

            var reverseFunctionName = FindMatch(sourceCode, @"([\w$]*)\s*:\s*function\s*\(\s*[\w$]*\s*\)\s*{\s*(?:return\s*)?[\w$]*\.reverse\s*\(\s*\)\s*}");
            var sliceFunctionName = FindMatch(sourceCode, @"([\w$]*)\s*:\s*function\s*\(\s*[\w$]*\s*,\s*[\w$]*\s*\)\s*{\s*(?:return\s*)?[\w$]*\.(?:slice|splice)\(.+\)\s*}");

            var functionCode = FindMatch(sourceCode, regExp);
            functionCode = functionCode.Replace(reverseFunctionName, "reverse");
            functionCode = functionCode.Replace(sliceFunctionName, "slice");
            var functionCodePieces = functionCode.Split(';');

            List<int> decodeArray = new List<int>();

            var regSlice = new Regex("slice\\s*\\(\\s*.+([0-9]+)\\s*\\)");
            string regSwap = "\\w+\\s*\\(\\s*\\w+\\s*,\\s*([0-9]+)\\s*\\)";
            string regInline = "\\w+\\[0\\]\\s*=\\s*\\w+\\[([0-9]+)\\s*%\\s*\\w+\\.length\\]";

            for (var i = 0; i < functionCodePieces.Length; i++)
            {

                functionCodePieces[i] = functionCodePieces[i].Trim();

                var codeLine = functionCodePieces[i];

                if (codeLine.Length > 0)
                {

                    var arrSlice = regSlice.Match(codeLine);

                    if (arrSlice.Success && arrSlice.Length >= 2)
                    {
                        var slice = int.Parse(arrSlice.Groups[1].Value);
                        decodeArray.Add(-slice);
                    }
                    else if (functionCodePieces[i].IndexOf("reverse") >= 0)
                    {
                        decodeArray.Add(0);
                    }
                    else if (codeLine.IndexOf("[0]") >= 0)
                    { // inline swap

                        if (i + 2 < functionCodePieces.Length && functionCodePieces[i + 1].IndexOf(".length") >= 0 && functionCodePieces[i + 1].IndexOf("[0]") >= 0)
                        {

                            var inline = FindMatch(functionCodePieces[i + 1], regInline);
                            decodeArray.Add(int.Parse(inline));

                            i += 2;

                        }
                    }
                    else if (codeLine.IndexOf(',') >= 0)
                    { // swap
                        var swap = FindMatch(codeLine, regSwap);
                        int swapVal = int.Parse(swap);

                        if (swapVal > 0)
                        {
                            decodeArray.Add(swapVal);
                        }

                    }
                }
            }
            return decodeArray;
        }

        private static async Task<string> DownloadStringAsync(string url)
        {
            var client = new HttpClient();
            return await client.GetStringAsync(url);
        }
        private static string DecryptSignature(string sig, List<int> arr)
        {
            var sigA = sig;

            for (var i = 0; i < arr.Count; i++)
            {
                var act = arr[i];
                sigA = (act > 0) ? Swap(sigA.ToCharArray(), act) : ((act == 0) ? Reverse(sigA) : sigA.Substring(-act));
            }

            return sigA;
        }

        private static string Swap(char[] a, int b)
        {
            var c = a[0];
            a[0] = a[b % a.Length];
            a[b] = c;

            return new string(a);
        }

        private static string Reverse(string s)
        {
            char[] charArray = s.ToCharArray();
            Array.Reverse(charArray);
            return new string(charArray);
        }
        private static string FindMatch(string text, string regexp)
        {
            Regex rgx = new Regex(regexp);
            var matches = rgx.Matches(text);

            return matches[0].Groups[1].Value;
        }

    }
}
