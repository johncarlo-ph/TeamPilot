export interface CommitDto {
  id: string;
  ticketId: string;
  branchName: string;
  commitHash: string;
  message: string;
  diffContent: string;
  authorAgentId: string | null;
  createdAtUtc: string;
}
