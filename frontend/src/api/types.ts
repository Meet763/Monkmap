export interface User {
  id: string;
  email: string;
  firstName: string;
  lastName: string;
  displayName: string;
  avatarUrl?: string | null;
}

export interface AuthResponse {
  token: string;
  user: User;
}

export interface WorkflowSummary {
  id: string;
  name: string;
  description?: string | null;
  isActive: boolean;
  createdAt: string;
  updatedAt: string;
  isLocked: boolean;
  lockedBy?: string | null;
  lockedAt?: string | null;
}

export interface WorkflowDetail {
  id: string;
  name: string;
  description?: string | null;
  isActive: boolean;
  activeVersionId?: string | null;
  graphJson: string;
  createdAt: string;
  updatedAt: string;
  isLocked: boolean;
  lockedBy?: string | null;
  lockedAt?: string | null;
}

export interface MemberRow {
  userId: string;
  email: string;
  name: string;
  permissions: string[];
  isOwner: boolean;
}

export interface InvitationRow {
  id: string;
  email: string;
  permissions: string[];
  expiresAt: string;
  createdAt: string;
}

export interface NodeParameter {
  name: string;
  displayName: string;
  type: "string" | "number" | "boolean" | "json" | "options" | "text" | "secret";
  required: boolean;
  default?: string | null;
  options?: string[] | null;
  placeholder?: string | null;
}

export interface NodeDefinition {
  type: string;
  displayName: string;
  group: string;
  icon: string;
  isTrigger: boolean;
  outputPorts: string[];
  parameters: NodeParameter[];
}

export interface IntegrationAction {
  key: string;
  displayName: string;
  description: string;
}

export interface IntegrationProvider {
  key: string;
  displayName: string;
  isConfigured: boolean;
  isConnected: boolean;
  actions: IntegrationAction[];
}

export interface ConnectionItem {
  id: string;
  provider: string;
  accountName: string;
  scopes: string[];
  createdAt: string;
  expiresAt?: string | null;
}

export interface ExecutionSummary {
  id: string;
  workflowId: string;
  status: string;
  triggerType: string;
  startedAt?: string | null;
  finishedAt?: string | null;
  error?: string | null;
}

export interface NodeExecution {
  id: string;
  nodeId: string;
  nodeType: string;
  status: string;
  inputJson?: string | null;
  outputJson?: string | null;
  error?: string | null;
  startedAt?: string | null;
  finishedAt?: string | null;
}

export interface ExecutionDetail extends ExecutionSummary {
  nodes: NodeExecution[];
}

/* The graph shape persisted in WorkflowVersion.graphJson */
export interface GraphNode {
  id: string;
  type: string;
  position: { x: number; y: number };
  parameters: Record<string, unknown>;
}
export interface GraphConnection {
  id: string;
  source: string;
  target: string;
  sourceHandle?: string | null;
  targetHandle?: string | null;
}
export interface WorkflowGraph {
  nodes: GraphNode[];
  connections: GraphConnection[];
}
