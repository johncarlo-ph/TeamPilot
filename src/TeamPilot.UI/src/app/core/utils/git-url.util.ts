/**
 * Builds a browsable URL to a branch on the remote (GitHub/GitLab `/tree/{branch}` convention -
 * the only two providers this app's Git integration supports, per IGitService's PAT-as-username
 * convention). Returns null when either input is missing.
 */
export function buildBranchUrl(remoteUrl: string | null | undefined, branchName: string | null | undefined): string | null {
  if (!remoteUrl || !branchName) {
    return null;
  }
  const webUrl = remoteUrl.replace(/\.git$/i, '');
  return `${webUrl}/tree/${branchName}`;
}
