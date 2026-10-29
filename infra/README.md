# Infrastructure

`main.bicep` describes everything in the `rg-teetime-demo` resource group: Log Analytics, Application Insights,
the SQL server (Entra-only) with the free-offer database, the Container Apps environment, the app, the migration
job and its identity.

```
# preview — changes nothing
az deployment group what-if -g rg-teetime-demo -f infra/main.bicep -p infra/main.bicepparam -p image=<current image>
# apply
az deployment group create  -g rg-teetime-demo -f infra/main.bicep -p infra/main.bicepparam -p image=<current image>
```

Pass the image that is running (`az containerapp show -g rg-teetime-demo -n teetime-booking --query
"properties.template.containers[0].image" -o tsv`), otherwise the deployment rolls the app back to `latest`.

## Not in the template

Three things live outside Bicep, because they are not Azure Resource Manager resources:

| What | Why outside | How it was set up |
|---|---|---|
| **Database users** for the identities | SQL objects, created with T-SQL | `CREATE USER [teetime-booking] FROM EXTERNAL PROVIDER` (read, write) and `[id-teetime-migrator]` (read, write, `db_ddladmin`), run as the Entra admin |
| **GitHub OIDC identity** | an Entra app registration (Microsoft Graph, not ARM) | app `github-tee-time-booking-demo`, federated credential for `repo:…:environment:production`, Contributor on this resource group |
| **Subscription budget** | subscription scope | $5 a year, email alerts |
