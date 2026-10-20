# Connect Hermes Agent

In **Settings → AI**, choose **Hermes Agent → Add provider**.

| Field | Value |
|---|---|
| Protocol | ChatCompletions |
| API base URL | `http://127.0.0.1:8642/v1/` |
| Chat model ID | `hermes-agent` |
| API key | Your Hermes `API_SERVER_KEY` |

Enable the API server in your existing Hermes installation, then start its gateway:

```dotenv
API_SERVER_ENABLED=true
API_SERVER_KEY=<your-own-private-server-key>
```

```text
hermes gateway
```

Save the provider and select it for chat. A remote gateway needs HTTPS. Naby does not install Hermes or configure its model and tool backends.

Hermes executes its own tools under its own permissions; Naby approvals cover Naby actions only. Clear creates a new chat session without deleting Hermes memory. Live voice requires a separate voice provider. Keys remain in Windows Credential Manager.

Stop cancels Naby's request. Manage any continuing backend jobs in Hermes.

[Official Hermes API server documentation](https://hermes-agent.nousresearch.com/docs/user-guide/features/api-server/).
