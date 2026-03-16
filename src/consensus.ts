/**
 * Consensus is detected ONLY when an agent explicitly writes the phrase
 * "WE HAVE CONSENSUS" (case-insensitive). This is a magic phrase that
 * both agents are instructed to use in their prompts. Partial agreement
 * like "I agree on one point" does NOT count.
 */

const CONSENSUS_PATTERN = /^\s*WE HAVE CONSENSUS\b/im;

export interface ConsensusResult {
  reached: boolean;
  reason: string;
}

export function checkConsensus(latestResponse: string): ConsensusResult {
  if (CONSENSUS_PATTERN.test(latestResponse)) {
    return {
      reached: true,
      reason: 'Agent declared "WE HAVE CONSENSUS"',
    };
  }

  return {
    reached: false,
    reason: "No consensus declaration found",
  };
}
