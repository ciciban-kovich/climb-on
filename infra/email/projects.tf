resource "scaleway_account_project" "env" {
  for_each = var.environments

  name        = "climbon-${each.key}"
  description = "climb-on ${each.key}: transactional email"
}
