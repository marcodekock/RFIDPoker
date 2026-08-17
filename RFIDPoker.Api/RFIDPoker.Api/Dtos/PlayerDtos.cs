namespace RFIDPoker.Api.Dtos;

public record PlayerNameDto(int SeatNumber, string Name, long? ChipCount = null);

public record SetPlayerNameRequest(string Name);

public record SetPlayerNamesRequest(List<PlayerNameDto> Players);

public record SetPlayerChipCountRequest(long? ChipCount);

public record SetBlindsRequest(string? Blinds);
