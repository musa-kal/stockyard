# IAM roles for service accounts. Each Stockyard workload gets its own role,
# trusted only by its own Kubernetes service account in the stockyard
# namespace, so a compromised pod cannot borrow another service's access.
resource "aws_iam_openid_connect_provider" "this" {
  url            = aws_eks_cluster.this.identity[0].oidc[0].issuer
  client_id_list = ["sts.amazonaws.com"]
}

locals {
  oidc_issuer = replace(aws_eks_cluster.this.identity[0].oidc[0].issuer, "https://", "")
}

data "aws_iam_policy_document" "irsa_assume" {
  for_each = local.service_policies

  statement {
    actions = ["sts:AssumeRoleWithWebIdentity"]

    principals {
      type        = "Federated"
      identifiers = [aws_iam_openid_connect_provider.this.arn]
    }

    condition {
      test     = "StringEquals"
      variable = "${local.oidc_issuer}:sub"
      values   = ["system:serviceaccount:${var.namespace}:${each.key}"]
    }

    condition {
      test     = "StringEquals"
      variable = "${local.oidc_issuer}:aud"
      values   = ["sts.amazonaws.com"]
    }
  }
}

resource "aws_iam_role" "irsa" {
  for_each = local.service_policies

  name               = "${var.cluster_name}-${trimprefix(each.key, "stockyard-")}"
  assume_role_policy = data.aws_iam_policy_document.irsa_assume[each.key].json
}

resource "aws_iam_role_policy" "irsa" {
  for_each = local.service_policies

  name   = "access"
  role   = aws_iam_role.irsa[each.key].id
  policy = each.value
}

# api: relays the outbox to the events queue, replays the DLQ back onto it,
# reads the pending count for /stock/reconcile, and runs on-demand exports.
data "aws_iam_policy_document" "api" {
  statement {
    sid       = "PublishEvents"
    actions   = ["sqs:SendMessage", "sqs:GetQueueAttributes"]
    resources = [var.queue_arn]
  }

  statement {
    sid       = "ReplayDeadLetters"
    actions   = ["sqs:ReceiveMessage", "sqs:DeleteMessage", "sqs:GetQueueAttributes"]
    resources = [var.dlq_arn]
  }

  statement {
    sid       = "WriteExports"
    actions   = ["s3:PutObject"]
    resources = ["${var.export_bucket_arn}/${var.export_prefix}/*"]
  }
}

# projections: the only consumer of the events queue.
data "aws_iam_policy_document" "projections" {
  statement {
    sid = "ConsumeEvents"
    actions = [
      "sqs:ReceiveMessage",
      "sqs:DeleteMessage",
      "sqs:ChangeMessageVisibility",
      "sqs:GetQueueAttributes",
    ]
    resources = [var.queue_arn]
  }
}

# notifier: reads the signing key and nothing else. It drives webhooks from
# WebhookDelivery rows in SQL Server, never from the queue (ADR 0004), so it
# carries no SQS permission at all.
data "aws_iam_policy_document" "notifier" {
  statement {
    sid       = "ReadSigningSecret"
    actions   = ["secretsmanager:GetSecretValue"]
    resources = [var.signing_secret_arn]
  }
}

# exporter: writes the nightly Parquet files. StockReconciled goes out through
# the outbox and the api relay, so no SQS here either.
data "aws_iam_policy_document" "exporter" {
  statement {
    sid       = "WriteExports"
    actions   = ["s3:PutObject"]
    resources = ["${var.export_bucket_arn}/${var.export_prefix}/*"]
  }
}
