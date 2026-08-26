# Application secrets. Terraform creates the containers only; the values are
# written out of band (docs/runbook.md) so they never pass through state. The
# Secrets Store CSI driver reads them into the pods and syncs the Kubernetes
# Secrets stockyard-db and stockyard-signing.
#
# Each secret holds a JSON object whose key is the environment variable name
# the services read, so the SecretProviderClass can map it one to one.

resource "aws_secretsmanager_secret" "db" {
  name                    = "${var.project}/${var.environment}/db"
  description             = "SQL Server connection string for the Stockyard services (ConnectionStrings__Stockyard)."
  recovery_window_in_days = var.secret_recovery_window_days
}

resource "aws_secretsmanager_secret" "signing" {
  name                    = "${var.project}/${var.environment}/signing"
  description             = "HMAC key the Notifier signs supplier webhooks with (Notifier__SigningSecret)."
  recovery_window_in_days = var.secret_recovery_window_days
}
