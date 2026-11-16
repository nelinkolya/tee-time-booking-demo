# Infrastructure

`main.bicep` describes everything in the `rg-teetime-demo` resource group: Log Analytics, Application Insights,
the SQL server (Entra-only) with the free-offer database, the Container Apps environment, the app, the migration
job and its identity, the Key Vault with the ticket-signing key, and the alert that emails when the app stops
scaling to zero.

```
# preview — changes nothing
az deployment group what-if -g rg-teetime-demo -f infra/main.bicep -p infra/main.bicepparam -p image=<current image>
# apply
az deployment group create  -g rg-teetime-demo -f infra/main.bicep -p infra/main.bicepparam -p image=<current image>
```

Pass the image that is running (`az containerapp show -g rg-teetime-demo -n teetime-booking --query
"properties.template.containers[0].image" -o tsv`), otherwise the deployment rolls the app back to `latest`.

## The ticket-signing key

The app signs booking tickets with `ticket-signing`, an EC P-256 key in `kv-teetime-<suffix>` (output
`ticketVaultName`). After a deployment:

```
vault=$(az keyvault list -g rg-teetime-demo --query "[0].name" -o tsv)

# the key and its versions; the private part cannot be shown, only the public one
az keyvault key show --vault-name $vault -n ticket-signing --query "{kid:key.kid, crv:key.crv}"
az keyvault key list-versions --vault-name $vault -n ticket-signing --query "[].{kid:kid, enabled:attributes.enabled}" -o table

# who may use it: the app's identity, the custom Ticket Signer role (read + sign), on this key only
az role assignment list --scope "$(az keyvault show -n $vault --query id -o tsv)/keys/ticket-signing" -o table

# rotate now (the policy rotates once a year, $1 each; by hand it is a few operations): new tickets get the new
# kid, old ones still verify (/.well-known/jwks.json lists both)
az keyvault key rotate --vault-name $vault -n ticket-signing

# retire a version: tickets it signed fail with reason UnknownKey once the app's key cache expires (10 minutes)
az keyvault key set-attributes --vault-name $vault -n ticket-signing --version <version> --enabled false
```

The alert `alert-teetime-vault-busy` emails when the vault answers over 2,000 requests in an hour: signatures are
billed, and normal use is tens an hour.

Who did what with the key, in Log Analytics:

```
AzureDiagnostics
| where ResourceProvider == "MICROSOFT.KEYVAULT"
| project TimeGenerated, OperationName, ResultType, identity_claim_oid_g, CallerIPAddress
```

Your own account needs a data-plane role to run the `key` commands above (the vault uses RBAC, so being Owner of
the resource group is not enough): `az role assignment create --role "Key Vault Crypto Officer" --assignee
<your sign-in> --scope <vault id>`.

## Not in the template

Three things live outside Bicep, because they are not Azure Resource Manager resources:

| What | Why outside | How it was set up |
|---|---|---|
| **Database users** for the identities | SQL objects, created with T-SQL | `CREATE USER [teetime-booking] FROM EXTERNAL PROVIDER` (read, write) and `[id-teetime-migrator]` (read, write, `db_ddladmin`), run as the Entra admin |
| **GitHub OIDC identity** | an Entra app registration (Microsoft Graph, not ARM) | app `github-tee-time-booking-demo`, federated credential for `repo:…:environment:production`, Contributor on this resource group |
| **Subscription budget** | subscription scope | $5 a year, email alerts |
