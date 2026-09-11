export interface GitFileDiff {
  filePath: string;
  patch: string;
}

export interface GitDiffResult {
  sourceBranch: string;
  targetBranch: string;
  files: GitFileDiff[];
}

export interface GitConflictingFile {
  filePath: string;
  conflictContent: string;
}

export interface GitMergeConflictResult {
  hasConflicts: boolean;
  conflictingFiles: GitConflictingFile[];
}
