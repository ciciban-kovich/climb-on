output "ci_client_ids" {
  description = "Client IDs of the CI identities, for the AZURE_CLIENT_ID_* repository variables."
  value       = { for name, identity in azurerm_user_assigned_identity.ci : name => identity.client_id }
}

output "ci_principal_ids" {
  description = "Principal (object) IDs of the CI identities, passed to infra/shared as variables."
  value       = { for name, identity in azurerm_user_assigned_identity.ci : name => identity.principal_id }
}

output "state_account_name" {
  description = "State storage account, for the TFSTATE_ACCOUNT repository variable."
  value       = azurerm_storage_account.tfstate.name
}
