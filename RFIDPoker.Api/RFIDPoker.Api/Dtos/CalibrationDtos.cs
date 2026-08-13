namespace RFIDPoker.Api.Dtos;

public record CardMappingDto(string TagId, int Rank, int Suit);

public record RegisterMappingRequest(string TagId, int Rank, int Suit);

public record AntennaReadingDto(string DeviceName, int AntennaIndex, string Function, List<string> TagIds);
