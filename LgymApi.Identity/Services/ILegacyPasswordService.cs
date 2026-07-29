namespace LgymApi.Application.Services;

public interface ILegacyPasswordService
{
    bool Verify(string password, string hash, string salt, int? iterations, int? keyLength, string? digest);
    (string Hash, string Salt, int Iterations, int KeyLength, string Digest) Create(string password);
}
