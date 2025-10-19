using System.Text.Json;
using SpotifyAPI.Web;

namespace LedMatrixOS.Apps.Services;

public static class SpotifyAuth
{
    private const string TokenFile = "./token.json";

    public static async Task<SpotifyClientConfig> GetClientConfig(string clientId, string clientSecret)
    {
        var token = await GetToken(clientId, clientSecret);
        var authenticator = new AuthorizationCodeAuthenticator(clientId, clientSecret, token);
        
        authenticator.TokenRefreshed += (sender, args) =>
        {
            File.WriteAllText(TokenFile, JsonSerializer.Serialize(args));
        };
        
        return SpotifyClientConfig
            .CreateDefault()
            .WithAuthenticator(authenticator);
    }

    private static async Task<AuthorizationCodeTokenResponse> GetToken(string clientId, string clientSecret)
    {
        if (File.Exists(TokenFile))
        {
            var tokenJson = await File.ReadAllTextAsync(TokenFile);
            var content = JsonSerializer.Deserialize<AuthorizationCodeTokenResponse>(tokenJson);
            if (content != null && !string.IsNullOrEmpty(content.AccessToken))
            {
                return content;
            }
        }
        
        var request = new LoginRequest(new Uri("http://localhost:9090/callback"), clientId, LoginRequest.ResponseType.Code)
        {
            Scope = new List<string> { Scopes.UserReadCurrentlyPlaying, Scopes.UserReadPlaybackState, Scopes.UserLibraryRead }
        };

        Console.WriteLine(request.ToUri());

        Console.WriteLine("Please open the following URL in your browser to authorize the application:");
        Console.WriteLine("Now paste in the code from the redirect URL");
        var code = Console.ReadLine();
        
        var config = SpotifyClientConfig.CreateDefault();
        var tokenResponse = await new OAuthClient(config).RequestToken(
            new AuthorizationCodeTokenRequest(
                clientId, clientSecret, code, new Uri("http://localhost:9090/callback")
            )
        );

        await File.WriteAllTextAsync(TokenFile, JsonSerializer.Serialize(tokenResponse));

        return tokenResponse;
    }
}
