/** Where a project is in cloning its remote repository - see `ProjectCard`'s progress UI, driven
 * by `ProjectEventType: 'ProjectCloneProgress'`. */
export type ProjectStatus = 'Cloning' | 'Ready' | 'Failed';

export interface ProjectDto {
  id: string;
  name: string;
  description: string;
  remoteUrl: string;
  status: ProjectStatus;
  cloneFailureReason: string | null;
  createdAtUtc: string;
  updatedAtUtc: string | null;
}

export interface CreateProjectRequest {
  name: string;
  description: string | null;
  remoteUrl: string;
  accessToken: string;
}

/** accessToken null/blank keeps the currently stored token - remoteUrl isn't included here
 * since it's immutable after creation (re-pointing it would orphan the existing clone). */
export interface UpdateProjectRequest {
  name: string;
  description: string | null;
  accessToken: string | null;
}
