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
