namespace RFIDPoker.Api.Dtos;

public record CardMappingDto(int DeckId, string DeckName, string TagId, int Rank, int Suit);

public record RegisterMappingRequest(int DeckId, string TagId, int Rank, int Suit);

public record DeleteMappingRequest(int DeckId, string TagId);

public record AntennaReadingDto(string DeviceName, int AntennaIndex, string Function, List<string> TagIds);
