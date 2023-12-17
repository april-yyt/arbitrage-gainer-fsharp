# Use Ubuntu as the base image
FROM ubuntu:latest

# Update and install necessary packages
RUN apt-get update && apt-get install -y \
    wget \
    openjdk-8-jdk \
    gnupg2

# Set Java environment variables
ENV JAVA_HOME=/usr/lib/jvm/java-8-openjdk-arm64
ENV PATH=$PATH:$JAVA_HOME/bin

# Install Hadoop
ENV HADOOP_VERSION 3.3.6
ENV HADOOP_URL https://downloads.apache.org/hadoop/common/hadoop-$HADOOP_VERSION/hadoop-$HADOOP_VERSION.tar.gz

RUN wget "$HADOOP_URL" -O /tmp/hadoop.tar.gz \
    && tar -xzf /tmp/hadoop.tar.gz -C /opt/ \
    && rm /tmp/hadoop.tar.gz

# Set Hadoop environment variables
ENV HADOOP_HOME=/opt/hadoop-$HADOOP_VERSION
ENV HADOOP_CONF_DIR=$HADOOP_HOME/etc/hadoop
ENV PATH=$PATH:$HADOOP_HOME/bin:$HADOOP_HOME/sbin

# # Install Mono and F#
# RUN apt install ca-certificates gnupg
# RUN gpg --homedir /tmp --no-default-keyring --keyring /usr/share/keyrings/mono-official-archive-keyring.gpg --keyserver hkp://keyserver.ubuntu.com:80 --recv-keys 3FA7E0328081BFF6A14DA29AA6A19B38D3D831EF
# RUN "deb [signed-by=/usr/share/keyrings/mono-official-archive-keyring.gpg] https://download.mono-project.com/repo/ubuntu stable-focal main" |  tee /etc/apt/sources.list.d/mono-official-stable.list
# RUN apt update

RUN apt-get update && apt-get install -y curl

# Install .NET SDK
# RUN wget https://dotnet.microsoft.com/download/dotnet/scripts/v1/dotnet-install.sh
# RUN bash dotnet-install.sh -c Current
# ENV PATH="$PATH:/root/.dotnet"
# Download the .NET SDK
RUN curl -SL -o dotnet-sdk.tar.gz https://download.visualstudio.microsoft.com/download/pr/ab281706-a07c-489e-b674-6067904e5d64/bf950572cc16cbbcd358a89023788404/dotnet-sdk-6.0.320-linux-arm64.tar.gz 

# Extract the SDK
RUN mkdir -p /usr/share/dotnet && tar -zxf dotnet-sdk.tar.gz -C /usr/share/dotnet

# Add dotnet to the PATH
ENV PATH="$PATH:/usr/share/dotnet"


# ENV DEBIAN_FRONTEND=noninteractive

# RUN apt-get install -y mono-devel
# RUN apt-get install -y fsharp


# Add F# to PATH
ENV PATH="/usr/local/fsharp/bin:${PATH}"

# Copy your F# scripts and data into the container
COPY mapper.fsx /app/mapper.fsx
COPY reducer.fsx /app/reducer.fsx
COPY historicalData.txt /app/historicalData.txt
# Copy the Newtonsoft.Json.dll into the container
COPY bin/Debug/net6.0/Newtonsoft.Json.dll /app/Newtonsoft.Json.dll

# Set the working directory
WORKDIR /app

# Make the F# scripts executable
# RUN chmod +x /app/mapper.fsx
# RUN chmod +x /app/reducer.fsx

