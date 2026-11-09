import * as signalR from "@microsoft/signalr";
import { TOKEN_KEY } from "@/api/client";

export function createExecutionConnection(): signalR.HubConnection {
  return new signalR.HubConnectionBuilder()
    .withUrl("/hubs/executions", {
      accessTokenFactory: () => localStorage.getItem(TOKEN_KEY) ?? "",
    })
    .withAutomaticReconnect()
    .build();
}
