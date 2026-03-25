using System.Text.Json;
using DEADSKY.AI.Client;

namespace DEADSKY.Backend.Tests;

public class ToolCallParsingTests
{
    [Fact]
    public void ToolCall_GetIntAndGetDouble_ParseNumericStrings()
    {
        var args = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(
            """
            {
              "range_nm": "120.5",
              "count": "2"
            }
            """)!;

        var call = new ToolCall
        {
            Name = "test",
            Arguments = args
        };

        Assert.Equal(120.5, call.GetDouble("range_nm"), 3);
        Assert.Equal(2, call.GetInt("count"));
    }
}
