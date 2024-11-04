using System.Text;
using Newtonsoft.Json;

namespace MusicBot;

internal class JSONReader
{
    public string prefix { get; set; }
    public string hostname { get; set; }
    public string password { get; set; }
    public int port { get; set; }
    public bool secure { get; set; }


    public async Task ReadJSON()
    {
        using (var sr = new StreamReader("config.json", new UTF8Encoding(false)))
        {
            var json = await sr.ReadToEndAsync();
            var obj = JsonConvert.DeserializeObject<ConfigJSON>(json);

            prefix = obj.Prefix;
            hostname = obj.Hostname;
            password = obj.Password;
            port = obj.Port;
            secure = obj.Secure;
        }
    }
}

internal sealed class ConfigJSON
{
    public string Prefix { get; set; }
    public string Hostname { get; set; }
    public string Password { get; set; }
    public int Port { get; set; }
    public bool Secure { get; set; }
}