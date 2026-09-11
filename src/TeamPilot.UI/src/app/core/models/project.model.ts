export interface ProjectDto {
  id: string;
  name: string;
  description: string;
  repositoryPath: string;
  createdAtUtc: string;
  updatedAtUtc: string | null;
}

export interface CreateProjectRequest {
  name: string;
  description: string | null;
  repositoryPath: string;
}

export interface UpdateProjectRequest {
  name: string;
  description: string | null;
  repositoryPath: string;
}
