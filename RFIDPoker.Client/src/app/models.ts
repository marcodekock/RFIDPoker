export interface Card {
  rank: number;
  suit: number;
}

export interface PlayerAnalysis {
  seatNumber: number;
  playerName: string;
  chipCount?: number | null;
  holeCards: Card[];
  handRank: number | null;
  handDescription: string;
  bestFiveCards: Card[];
  winPercentage: number;
  tiePercentage: number;
  losePercentage: number;
  isFolded: boolean;
}

export interface AnalysisResult {
  currentStreet: number;
  blinds?: string | null;
  communityCards: Card[];
  muckedCards: Card[];
  activePlayers: PlayerAnalysis[];
  foldedPlayers: PlayerAnalysis[];
  activePlayerCount: number;
  headsUpOuts?: HeadsUpOuts | null;
  break?: BreakState | null;
  timestamp: string;
}

export interface HeadsUpOuts {
  seatNumber: number;
  playerName: string;
  outs: Card[];
}

export interface BreakState {
  isActive: boolean;
  isPaused: boolean;
  label?: string | null;
  totalSeconds: number;
  remainingSeconds: number;
  serverNowUtc: string;
}

export interface CardMapping {
  tagId: string;
  rank: number;
  suit: number;
}

export interface AntennaReading {
  deviceName: string;
  antennaIndex: number;
  function: string;
  tagIds: string[];
}

export const RANK_NAMES: Record<number, string> = {
  2: '2', 3: '3', 4: '4', 5: '5', 6: '6', 7: '7', 8: '8', 9: '9',
  10: '10', 11: 'J', 12: 'Q', 13: 'K', 14: 'A'
};

export const SUIT_NAMES: Record<number, string> = {
  0: 'Hearts', 1: 'Diamonds', 2: 'Clubs', 3: 'Spades'
};

export const SUIT_SYMBOLS: Record<number, string> = {
  0: '♥', 1: '♦', 2: '♣', 3: '♠'
};

export const STREET_NAMES: Record<number, string> = {
  0: 'Pre-Flop', 1: 'Flop', 2: 'Turn', 3: 'River'
};
